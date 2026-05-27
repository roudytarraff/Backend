using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TripPlanner.Api.Data;
using TripPlanner.Api.Domain;
using TripPlanner.Api.Domain.Events;

namespace Backend.Services.Push;

public sealed class PushNotificationService
{
    private readonly AppDbContext _db;
    private readonly ILogger<PushNotificationService> _logger;
    private readonly FirebasePushOptions _options;
    private readonly Lazy<FirebaseMessaging?> _messaging;

    public PushNotificationService(
        AppDbContext db,
        IOptions<FirebasePushOptions> options,
        ILogger<PushNotificationService> logger)
    {
        _db = db;
        _logger = logger;
        _options = options.Value;
        _messaging = new Lazy<FirebaseMessaging?>(CreateMessaging);
    }

    public bool IsConfigured => _messaging.Value is not null;

    public async Task SendToEventAsync(
        Guid eventId,
        string title,
        string body,
        Dictionary<string, string>? data = null,
        Guid? excludeUserId = null,
        CancellationToken ct = default)
    {
        var userIds = await _db.EventMembers
            .AsNoTracking()
            .Where(m => m.EventId == eventId && m.Status == MembershipStatus.Active)
            .Where(m => excludeUserId == null || m.UserId != excludeUserId.Value)
            .Select(m => m.UserId)
            .Distinct()
            .ToListAsync(ct);

        await SendToUsersAsync(userIds, title, body, data, ct);
    }

    public async Task SendToEventMembersAsync(
        Guid eventId,
        IEnumerable<Guid> memberIds,
        string title,
        string body,
        Dictionary<string, string>? data = null,
        CancellationToken ct = default)
    {
        var ids = memberIds.Distinct().ToList();
        if (ids.Count == 0) return;

        var userIds = await _db.EventMembers
            .AsNoTracking()
            .Where(m => m.EventId == eventId && ids.Contains(m.EventMemberId) && m.Status == MembershipStatus.Active)
            .Select(m => m.UserId)
            .Distinct()
            .ToListAsync(ct);

        await SendToUsersAsync(userIds, title, body, data, ct);
    }

    public async Task SendToUsersAsync(
        IEnumerable<Guid> userIds,
        string title,
        string body,
        Dictionary<string, string>? data = null,
        CancellationToken ct = default)
    {
        var messaging = _messaging.Value;
        if (messaging is null) return;

        var users = userIds.Distinct().ToList();
        if (users.Count == 0) return;

        var tokens = await _db.DevicePushTokens
            .Where(t => users.Contains(t.UserId) && t.IsActive)
            .Select(t => t.Token)
            .Distinct()
            .ToListAsync(ct);

        if (tokens.Count == 0) return;

        var payload = NormalizeData(data);
        var staleTokens = new List<string>();

        foreach (var token in tokens)
        {
            try
            {
                var isDriverCall = payload.TryGetValue("type", out var type) && type == "driver-call";
                await messaging.SendAsync(new Message
                {
                    Token = token,
                    Notification = isDriverCall
                        ? null
                        : new Notification
                    {
                        Title = title,
                        Body = body
                    },
                    Data = payload,
                    Android = new AndroidConfig
                    {
                        Priority = Priority.High,
                        Notification = new AndroidNotification
                        {
                            ChannelId = isDriverCall ? "tripmate-calls" : "tripmate-live"
                        }
                    },
                    Apns = new ApnsConfig
                    {
                        Headers = new Dictionary<string, string>
                        {
                            ["apns-priority"] = "10"
                        },
                        Aps = new Aps
                        {
                            Sound = "default",
                            ContentAvailable = true
                        }
                    }
                }, ct);
            }
            catch (FirebaseMessagingException ex) when (
                ex.MessagingErrorCode is MessagingErrorCode.Unregistered or MessagingErrorCode.InvalidArgument)
            {
                staleTokens.Add(token);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send push notification.");
            }
        }

        if (staleTokens.Count > 0)
        {
            await _db.DevicePushTokens
                .Where(t => staleTokens.Contains(t.Token))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.IsActive, false)
                    .SetProperty(t => t.LastSeenAtUtc, DateTime.UtcNow), ct);
        }
    }

    private FirebaseMessaging? CreateMessaging()
    {
        try
        {
            var json = _options.ServiceAccountJson;
            if (string.IsNullOrWhiteSpace(json) && !string.IsNullOrWhiteSpace(_options.ServiceAccountBase64))
            {
                json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(_options.ServiceAccountBase64));
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                _logger.LogWarning("Firebase push is disabled. Set Firebase:ServiceAccountJson or Firebase:ServiceAccountBase64.");
                return null;
            }

            var appName = string.IsNullOrWhiteSpace(_options.ProjectId)
                ? "tripmate-push"
                : $"tripmate-push-{_options.ProjectId}";
            FirebaseApp? app = null;
            try
            {
                app = FirebaseApp.GetInstance(appName);
            }
            catch
            {
                app = null;
            }

            app ??= FirebaseApp.Create(new AppOptions
            {
                Credential = GoogleCredential.FromJson(json),
                ProjectId = _options.ProjectId
            }, appName);

            return FirebaseMessaging.GetMessaging(app);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Firebase push could not be initialized.");
            return null;
        }
    }

    private static Dictionary<string, string> NormalizeData(Dictionary<string, string>? data)
    {
        var normalized = new Dictionary<string, string>();
        if (data is null) return normalized;

        foreach (var (key, value) in data)
        {
            if (!string.IsNullOrWhiteSpace(key) && value is not null)
            {
                normalized[key] = value;
            }
        }

        return normalized;
    }
}
