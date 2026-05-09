using System.Security.Claims;
using Backend.Hubs;
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
    private readonly AppDbContext _db;
    private readonly IBlobStorageService _blobStorage;
    private readonly IHubContext<EventHub> _hubContext;

    public EventExperienceController(AppDbContext db, IBlobStorageService blobStorage, IHubContext<EventHub> hubContext)
    {
        _db = db;
        _blobStorage = blobStorage;
        _hubContext = hubContext;
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
                .Where(m => m.ChatRoomId == ev.ChatRoom.ChatRoomId && !m.IsDeleted && memberIds.Contains(m.SentByEventMemberId))
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

        var ev = await _db.Events
            .Include(e => e.ChatRoom)
            .Include(e => e.Organizers)
            .Include(e => e.Participants)
            .FirstOrDefaultAsync(e => e.EventId == eventId, ct);

        if (ev is null) return NotFound("Event not found.");
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
