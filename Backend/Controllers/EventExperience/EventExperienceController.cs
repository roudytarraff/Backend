using System.Security.Claims;
using Backend.Hubs;
using Backend.Services.Billing;
using Backend.Services.Push;
using Backend.Services.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TripPlanner.Api.Data;
using TripPlanner.Api.Domain;
using TripPlanner.Api.Domain.Chat;
using TripPlanner.Api.Domain.Events;
using TripPlanner.Api.Domain.Media;

namespace Backend.Controllers.EventExperience;

[ApiController]
[Route("api/events/{eventId:guid}")]
[Authorize]
public sealed class EventExperienceController : ControllerBase
{
    private const string DriverChatPrefix = "[[driver-chat:";

    private readonly AppDbContext _db;
    private readonly IBlobStorageService _blobStorage;
    private readonly IHubContext<EventHub> _hubContext;
    private readonly PlanLimitService _plans;
    private readonly PushNotificationService _push;

    public EventExperienceController(AppDbContext db, IBlobStorageService blobStorage, IHubContext<EventHub> hubContext, PlanLimitService plans, PushNotificationService push)
    {
        _db = db;
        _blobStorage = blobStorage;
        _hubContext = hubContext;
        _plans = plans;
        _push = push;
    }

    [HttpGet("chat")]
    public async Task<IActionResult> GetChat(Guid eventId, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();

        var ev = await _db.Events
            .AsNoTracking()
            .Include(e => e.ChatRoom)
            .Include(e => e.Organizers)
            .Include(e => e.Participants)
            .Include(e => e.EventDays)
                .ThenInclude(d => d.Activities)
            .FirstOrDefaultAsync(e => e.EventId == eventId, ct);

        if (ev is null) return NotFound("Event not found.");
        if (!IsMember(ev, userId.Value)) return Forbid();
        if (IsPassiveParticipant(ev, userId.Value))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Passive participants cannot use event chat." });
        }

        var memberIds = ev.Organizers.Cast<EventMember>()
            .Concat(ev.Participants)
            .Where(m => m.Status == MembershipStatus.Active)
            .Select(m => m.EventMemberId)
            .ToList();

        var userIds = ev.Organizers.Cast<EventMember>()
            .Concat(ev.Participants)
            .Where(m => m.Status == MembershipStatus.Active)
            .Select(m => m.UserId)
            .Distinct()
            .ToList();

        var users = await _db.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.UserId))
            .ToDictionaryAsync(u => u.UserId, ct);

        var members = ev.Organizers.Cast<EventMember>()
            .Concat(ev.Participants)
            .ToDictionary(m => m.EventMemberId, m => m);

        var messages = ev.ChatRoom is null
            ? new List<ChatMessageDto>()
            : await _db.ChatMessages
                .AsNoTracking()
                .Where(m => m.ChatRoomId == ev.ChatRoom.ChatRoomId && !m.IsDeleted && memberIds.Contains(m.SentByEventMemberId) && !m.Content.StartsWith(DriverChatPrefix))
                .OrderBy(m => m.SentAt)
                .Take(150)
                .Select(m => new ChatMessageDto
                {
                    ChatMessageId = m.ChatMessageId,
                    Type = "message",
                    SenderMemberId = m.SentByEventMemberId,
                    Content = m.Content,
                    SentAt = m.SentAt
                })
                .ToListAsync(ct);

        foreach (var message in messages)
        {
            if (!members.TryGetValue(message.SenderMemberId!.Value, out var member)) continue;
            users.TryGetValue(member.UserId, out var user);
            message.SenderName = user is null ? "Event member" : $"{user.FirstName} {user.LastName}";
            message.SenderAvatarUrl = user?.ProfilePictureUrl;
            message.IsMine = member.UserId == userId.Value;
        }

        var systemMessages = ev.EventDays
            .SelectMany(d => d.Activities)
            .Where(a => a.Status != ActivityStatus.NotStarted)
            .Select(a => new ChatMessageDto
            {
                ChatMessageId = a.ActivityId,
                Type = "system",
                SenderName = "System",
                Content = a.Status == ActivityStatus.Ongoing
                    ? $"Activity started: {a.Title}"
                    : $"Activity ended: {a.Title}",
                SentAt = a.Status == ActivityStatus.Ended
                    ? a.EndTime ?? a.StartTime ?? ev.CreatedAt
                    : a.StartTime ?? a.EndTime ?? ev.CreatedAt,
                ActivityWindow = FormatWindow(a.StartTime, a.EndTime)
            })
            .ToList();

        return Ok(new
        {
            ev.EventId,
            ev.Title,
            MembersCount = memberIds.Count,
            Messages = messages.Concat(systemMessages).OrderBy(m => m.SentAt).ToList()
        });
    }

    [HttpPost("chat/messages")]
    public async Task<IActionResult> SendMessage(Guid eventId, [FromBody] SendChatMessageRequest req, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(req.Content)) return BadRequest("Message is required.");
        if (IsDriverChatContent(req.Content)) return BadRequest("Invalid message content.");

        var ev = await _db.Events
            .Include(e => e.ChatRoom)
            .Include(e => e.Organizers)
            .Include(e => e.Participants)
            .FirstOrDefaultAsync(e => e.EventId == eventId, ct);

        if (ev is null) return NotFound("Event not found.");
        if (IsPassiveParticipant(ev, userId.Value))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Passive participants cannot send event chat messages." });
        }
        if (ev.ChatRoom is null)
        {
            _db.ChatRooms.Add(new ChatRoom(eventId));
            await _db.SaveChangesAsync(ct);
            await _db.Entry(ev).Reference(e => e.ChatRoom).LoadAsync(ct);
        }

        ChatMessage msg;
        try
        {
            msg = ev.SendMessage(userId.Value, req.Content.Trim());
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }

        await _db.SaveChangesAsync(ct);

        var sender = ev.Organizers.Cast<EventMember>().Concat(ev.Participants).First(m => m.UserId == userId.Value);
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId.Value, ct);
        var dto = new ChatMessageDto
        {
            ChatMessageId = msg.ChatMessageId,
            Type = "message",
            SenderMemberId = sender.EventMemberId,
            SenderName = user is null ? "Event member" : $"{user.FirstName} {user.LastName}",
            SenderAvatarUrl = user?.ProfilePictureUrl,
            Content = msg.Content,
            SentAt = msg.SentAt
        };

        await _hubContext.Clients.Group($"event-{eventId}").SendAsync("ChatMessageReceived", new
        {
            EventId = eventId,
            Message = dto
        }, ct);

        await _push.SendToEventAsync(
            eventId,
            ev.Title,
            $"{dto.SenderName}: {dto.Content}",
            new Dictionary<string, string>
            {
                ["type"] = "event-chat",
                ["eventId"] = eventId.ToString(),
                ["messageId"] = dto.ChatMessageId.ToString()
            },
            userId.Value,
            ct);

        return Ok(dto);
    }

    [HttpGet("driver-chat/{driverParticipantId:guid}")]
    public async Task<IActionResult> GetDriverChat(Guid eventId, Guid driverParticipantId, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();
        await _plans.EnsureDriverCallsAllowed(_db, eventId, ct);

        var ev = await _db.Events
            .AsNoTracking()
            .Include(e => e.ChatRoom)
            .Include(e => e.Organizers)
            .Include(e => e.Participants)
            .FirstOrDefaultAsync(e => e.EventId == eventId, ct);

        if (ev is null) return NotFound("Event not found.");

        var driver = FindActiveMember(ev, driverParticipantId);
        if (driver is null) return NotFound("Driver not found.");
        if (IsOrganizer(driver)) return BadRequest("Organizer drivers use the event chat.");

        var actor = GetDriverChatActor(ev, userId.Value, driverParticipantId);
        if (actor is null) return Forbid();

        var participantIds = ev.Organizers.Cast<EventMember>()
            .Append(driver)
            .Where(m => m.Status == MembershipStatus.Active)
            .Select(m => m.EventMemberId)
            .Distinct()
            .ToList();

        var userIds = ev.Organizers.Cast<EventMember>()
            .Append(driver)
            .Where(m => m.Status == MembershipStatus.Active)
            .Select(m => m.UserId)
            .Distinct()
            .ToList();

        var users = await _db.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.UserId))
            .ToDictionaryAsync(u => u.UserId, ct);

        var members = ev.Organizers.Cast<EventMember>()
            .Append(driver)
            .Where(m => m.Status == MembershipStatus.Active)
            .GroupBy(m => m.EventMemberId)
            .ToDictionary(g => g.Key, g => g.First());

        var token = DriverChatToken(driverParticipantId);
        var messages = ev.ChatRoom is null
            ? new List<ChatMessageDto>()
            : await _db.ChatMessages
                .AsNoTracking()
                .Where(m => m.ChatRoomId == ev.ChatRoom.ChatRoomId && !m.IsDeleted && participantIds.Contains(m.SentByEventMemberId) && m.Content.StartsWith(token))
                .OrderBy(m => m.SentAt)
                .Take(150)
                .Select(m => new ChatMessageDto
                {
                    ChatMessageId = m.ChatMessageId,
                    Type = "message",
                    SenderMemberId = m.SentByEventMemberId,
                    Content = m.Content,
                    SentAt = m.SentAt
                })
                .ToListAsync(ct);

        foreach (var message in messages)
        {
            message.Content = StripDriverChatToken(message.Content);
            if (!members.TryGetValue(message.SenderMemberId!.Value, out var member)) continue;
            users.TryGetValue(member.UserId, out var user);
            message.SenderName = user is null ? "Event member" : $"{user.FirstName} {user.LastName}";
            message.SenderAvatarUrl = user?.ProfilePictureUrl;
            message.IsMine = member.UserId == userId.Value;
        }

        return Ok(new
        {
            ev.EventId,
            ev.Title,
            DriverParticipantId = driverParticipantId,
            MembersCount = participantIds.Count,
            Messages = messages
        });
    }

    [HttpPost("driver-chat/{driverParticipantId:guid}/messages")]
    public async Task<IActionResult> SendDriverMessage(Guid eventId, Guid driverParticipantId, [FromBody] SendChatMessageRequest req, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();
        await _plans.EnsureDriverCallsAllowed(_db, eventId, ct);

        var content = req.Content?.Trim();
        if (string.IsNullOrWhiteSpace(content)) return BadRequest("Message is required.");
        if (IsDriverChatContent(content)) return BadRequest("Invalid message content.");

        var token = DriverChatToken(driverParticipantId);
        if (token.Length + content.Length > 2000)
        {
            return BadRequest("Message is too long.");
        }

        var ev = await _db.Events
            .Include(e => e.ChatRoom)
            .Include(e => e.Organizers)
            .Include(e => e.Participants)
            .FirstOrDefaultAsync(e => e.EventId == eventId, ct);

        if (ev is null) return NotFound("Event not found.");

        var driver = FindActiveMember(ev, driverParticipantId);
        if (driver is null) return NotFound("Driver not found.");
        if (IsOrganizer(driver)) return BadRequest("Organizer drivers use the event chat.");

        var sender = GetDriverChatActor(ev, userId.Value, driverParticipantId);
        if (sender is null) return Forbid();

        if (ev.ChatRoom is null)
        {
            _db.ChatRooms.Add(new ChatRoom(eventId));
            await _db.SaveChangesAsync(ct);
            await _db.Entry(ev).Reference(e => e.ChatRoom).LoadAsync(ct);
        }

        var msg = new ChatMessage(ev.ChatRoom!.ChatRoomId, sender.EventMemberId, token + content);
        _db.ChatMessages.Add(msg);
        await _db.SaveChangesAsync(ct);

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId.Value, ct);
        var dto = new ChatMessageDto
        {
            ChatMessageId = msg.ChatMessageId,
            Type = "message",
            SenderMemberId = sender.EventMemberId,
            SenderName = user is null ? "Event member" : $"{user.FirstName} {user.LastName}",
            SenderAvatarUrl = user?.ProfilePictureUrl,
            Content = content,
            SentAt = msg.SentAt,
            IsMine = true
        };

        var broadcastDto = new ChatMessageDto
        {
            ChatMessageId = dto.ChatMessageId,
            Type = dto.Type,
            SenderMemberId = dto.SenderMemberId,
            SenderName = dto.SenderName,
            SenderAvatarUrl = dto.SenderAvatarUrl,
            Content = dto.Content,
            SentAt = dto.SentAt,
            IsMine = false
        };

        await _hubContext.Clients.Group($"event-{eventId}").SendAsync("DriverChatMessageReceived", new
        {
            EventId = eventId,
            DriverParticipantId = driverParticipantId,
            Message = broadcastDto
        }, ct);

        await _push.SendToEventMembersAsync(
            eventId,
            ev.Organizers.Cast<EventMember>().Append(driver).Where(m => m.EventMemberId != sender.EventMemberId).Select(m => m.EventMemberId),
            "Driver chat",
            $"{dto.SenderName}: {dto.Content}",
            new Dictionary<string, string>
            {
                ["type"] = "driver-chat",
                ["eventId"] = eventId.ToString(),
                ["driverParticipantId"] = driverParticipantId.ToString(),
                ["messageId"] = dto.ChatMessageId.ToString()
            },
            ct);

        return Ok(dto);
    }

    [HttpGet("media")]
    public async Task<IActionResult> GetMedia(Guid eventId, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();

        var ev = await _db.Events
            .AsNoTracking()
            .Include(e => e.Organizers)
            .Include(e => e.Participants)
            .FirstOrDefaultAsync(e => e.EventId == eventId, ct);

        if (ev is null) return NotFound("Event not found.");
        if (!IsMember(ev, userId.Value)) return Forbid();

        var memberUserIds = ev.Organizers.Cast<EventMember>()
            .Concat(ev.Participants)
            .Select(m => new { m.EventMemberId, m.UserId })
            .ToDictionary(x => x.EventMemberId, x => x.UserId);

        var users = await _db.Users
            .AsNoTracking()
            .Where(u => memberUserIds.Values.Contains(u.UserId))
            .ToDictionaryAsync(u => u.UserId, ct);

        var media = await _db.EventMedia
            .AsNoTracking()
            .Where(m => m.EventId == eventId)
            .OrderByDescending(m => m.UploadedAt)
            .Select(m => new EventMediaDto
            {
                MediaId = m.MediaId,
                MediaType = m.MediaType,
                FileUrl = m.FileUrl,
                UploadedAt = m.UploadedAt,
                UploadedByMemberId = m.UploadedByEventMemberId
            })
            .ToListAsync(ct);

        foreach (var item in media)
        {
            if (!memberUserIds.TryGetValue(item.UploadedByMemberId, out var uploaderUserId)) continue;
            if (!users.TryGetValue(uploaderUserId, out var user)) continue;
            item.UploadedByName = $"{user.FirstName} {user.LastName}";
            item.UploaderAvatarUrl = user.ProfilePictureUrl;
        }

        return Ok(media);
    }

    [HttpPost("media")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadMedia(Guid eventId, [FromForm] UploadEventMediaRequest req, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();
        if (req.File is null) return BadRequest("Media file is required.");

        var ev = await _db.Events
            .Include(e => e.EventMedia)
            .Include(e => e.Organizers)
            .Include(e => e.Participants)
            .FirstOrDefaultAsync(e => e.EventId == eventId, ct);

        if (ev is null) return NotFound("Event not found.");

        var mediaType = req.File.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ? "video" : "image";
        var fileUrl = await _blobStorage.UploadMediaAsync(req.File, $"events/{eventId}/media", ct);

        EventMedia media;
        try
        {
            media = ev.UploadMedia(userId.Value, mediaType, fileUrl);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }

        await _db.SaveChangesAsync(ct);

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId.Value, ct);
        var dto = new EventMediaDto
        {
            MediaId = media.MediaId,
            MediaType = media.MediaType,
            FileUrl = media.FileUrl,
            UploadedAt = media.UploadedAt,
            UploadedByMemberId = media.UploadedByEventMemberId,
            UploadedByName = user is null ? "Event member" : $"{user.FirstName} {user.LastName}",
            UploaderAvatarUrl = user?.ProfilePictureUrl
        };

        await _hubContext.Clients.Group($"event-{eventId}").SendAsync("EventMediaUploaded", new
        {
            EventId = eventId,
            Media = dto
        }, ct);

        return Ok(dto);
    }

    private Guid? GetUserIdFromClaims()
    {
        var uid = User.FindFirstValue("uid");
        return Guid.TryParse(uid, out var userId) ? userId : null;
    }

    private static bool IsMember(Event ev, Guid userId)
        => ev.Organizers.Any(o => o.UserId == userId && o.Status == MembershipStatus.Active) ||
           ev.Participants.Any(p => p.UserId == userId && p.Status == MembershipStatus.Active);

    private static bool IsPassiveParticipant(Event ev, Guid userId)
        => ev.Participants.Any(p => p.UserId == userId && p.Status == MembershipStatus.Active && p.Mode == ParticipantMode.Passive);

    private static EventMember? GetDriverChatActor(Event ev, Guid userId, Guid driverParticipantId)
    {
        var organizer = ev.Organizers.FirstOrDefault(o => o.UserId == userId && o.Status == MembershipStatus.Active);
        if (organizer is not null) return organizer;

        var driver = FindActiveMember(ev, driverParticipantId);
        return driver?.UserId == userId ? driver : null;
    }

    private static EventMember? FindActiveMember(Event ev, Guid eventMemberId)
        => ev.Organizers.Cast<EventMember>()
            .Concat(ev.Participants)
            .FirstOrDefault(m => m.EventMemberId == eventMemberId && m.Status == MembershipStatus.Active);

    private static bool IsOrganizer(EventMember member)
        => member is Organizer;

    private static string DriverChatToken(Guid driverParticipantId)
        => $"{DriverChatPrefix}{driverParticipantId:N}]]";

    private static bool IsDriverChatContent(string? content)
        => content?.StartsWith(DriverChatPrefix, StringComparison.Ordinal) == true;

    private static string StripDriverChatToken(string content)
    {
        if (!IsDriverChatContent(content)) return content;
        var tokenEnd = content.IndexOf("]]", StringComparison.Ordinal);
        return tokenEnd < 0 ? content : content[(tokenEnd + 2)..];
    }

    private static string? FormatWindow(DateTime? start, DateTime? end)
    {
        if (start is null && end is null) return null;
        var left = start?.ToString("HH:mm") ?? "?";
        var right = end?.ToString("HH:mm") ?? "?";
        return $"{left} - {right}";
    }
}

public sealed class SendChatMessageRequest
{
    public string Content { get; set; } = string.Empty;
}

public sealed class UploadEventMediaRequest
{
    public IFormFile? File { get; set; }
}

public sealed class ChatMessageDto
{
    public Guid ChatMessageId { get; set; }
    public string Type { get; set; } = "message";
    public Guid? SenderMemberId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string? SenderAvatarUrl { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
    public bool IsMine { get; set; }
    public string? ActivityWindow { get; set; }
}

public sealed class EventMediaDto
{
    public Guid MediaId { get; set; }
    public string MediaType { get; set; } = string.Empty;
    public string FileUrl { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; }
    public Guid UploadedByMemberId { get; set; }
    public string UploadedByName { get; set; } = "Event member";
    public string? UploaderAvatarUrl { get; set; }
}
