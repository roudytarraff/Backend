using System.Security.Claims;
using Backend.Services.Voice;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TripPlanner.Api.Data;
using TripPlanner.Api.Domain;
using TripPlanner.Api.Domain.Events;

namespace Backend.Controllers.EventVoice;

[ApiController]
[Route("api/events/{eventId:guid}/voice")]
[Authorize]
public sealed class EventVoiceController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly LiveKitTokenService _liveKit;

    public EventVoiceController(AppDbContext db, LiveKitTokenService liveKit)
    {
        _db = db;
        _liveKit = liveKit;
    }

    [HttpPost("token")]
    public async Task<IActionResult> Token(Guid eventId, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();

        if (!_liveKit.IsConfigured)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = "LiveKit is not configured. Add LiveKit:ServerUrl, LiveKit:ApiKey, and LiveKit:ApiSecret."
            });
        }

        var member = await _db.EventMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m =>
                m.EventId == eventId &&
                m.UserId == userId.Value &&
                m.Status == MembershipStatus.Active,
                ct);

        if (member is null)
        {
            var eventExists = await _db.Events.AsNoTracking().AnyAsync(e => e.EventId == eventId, ct);
            return eventExists ? Forbid() : NotFound("Event not found.");
        }

        var ev = await _db.Events
            .AsNoTracking()
            .Where(e => e.EventId == eventId)
            .Select(e => new
            {
                e.EventId,
                e.Title,
                e.Status,
                e.OwnerOrganizerId
            })
            .FirstOrDefaultAsync(ct);

        if (ev is null) return NotFound("Event not found.");
        if (ev.Status != EventStatus.Active) return BadRequest("Voice is available only while the event is active.");
        if (member is Participant { Mode: ParticipantMode.Passive })
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Passive participants cannot join event walkie talkie." });
        }

        var user = await _db.Users
            .AsNoTracking()
            .Where(u => u.UserId == userId.Value)
            .Select(u => new { u.FirstName, u.LastName, u.ProfilePictureUrl })
            .FirstOrDefaultAsync(ct);

        var role = ev.OwnerOrganizerId == member.EventMemberId ? "Owner" : member is Organizer ? "Organizer" : "Participant";
        var displayName = user is null
            ? "Event member"
            : $"{user.FirstName} {user.LastName}".Trim();
        var roomName = $"event-{eventId}";
        var identity = $"{eventId}:{member.EventMemberId}:{Guid.NewGuid():N}";
        var token = _liveKit.CreateJoinToken(roomName, identity, displayName, new
        {
            eventId,
            member.EventMemberId,
            member.UserId,
            role,
            displayName,
            profilePictureUrl = user?.ProfilePictureUrl
        });

        return Ok(new
        {
            serverUrl = _liveKit.ServerUrl,
            token,
            roomName,
            identity,
            displayName,
            role
        });
    }

    [HttpPost("driver-call/request")]
    public async Task<IActionResult> RequestDriverCall(Guid eventId, [FromBody] DriverCallRequest req, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();

        var ev = await _db.Events
            .AsNoTracking()
            .Include(e => e.Organizers)
            .Include(e => e.Participants)
            .FirstOrDefaultAsync(e => e.EventId == eventId, ct);

        if (ev is null) return NotFound("Event not found.");
        var organizer = ev.Organizers.FirstOrDefault(o => o.UserId == userId.Value && o.Status == MembershipStatus.Active);
        if (organizer is null) return Forbid();

        var driver = ev.Participants.FirstOrDefault(p =>
            p.EventMemberId == req.DriverParticipantId &&
            p.Status == MembershipStatus.Active &&
            p.Mode == ParticipantMode.Passive);
        if (driver is null) return NotFound("Driver participant not found.");

        await HttpContext.RequestServices.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<Hubs.EventHub>>()
            .Clients.Group($"event-{eventId}")
            .SendAsync("DriverCallRequested", new
            {
                EventId = eventId,
                req.ActivityId,
                DriverParticipantId = driver.EventMemberId,
                RequestedByMemberId = organizer.EventMemberId,
                RequestedAt = DateTime.UtcNow
            }, ct);

        return Ok();
    }

    [HttpPost("driver-call/token")]
    public async Task<IActionResult> DriverCallToken(Guid eventId, [FromBody] DriverCallRequest req, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();

        if (!_liveKit.IsConfigured)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = "LiveKit is not configured. Add LiveKit:ServerUrl, LiveKit:ApiKey, and LiveKit:ApiSecret."
            });
        }

        var ev = await _db.Events
            .AsNoTracking()
            .Include(e => e.Organizers)
            .Include(e => e.Participants)
            .FirstOrDefaultAsync(e => e.EventId == eventId, ct);

        if (ev is null) return NotFound("Event not found.");
        if (ev.Status != EventStatus.Active) return BadRequest("Driver calls are available only while the event is active.");

        var caller = ev.Organizers.Cast<EventMember>().Concat(ev.Participants).FirstOrDefault(m =>
            m.UserId == userId.Value &&
            m.Status == MembershipStatus.Active);
        if (caller is null) return Forbid();

        var driver = ev.Participants.FirstOrDefault(p =>
            p.EventMemberId == req.DriverParticipantId &&
            p.Status == MembershipStatus.Active &&
            p.Mode == ParticipantMode.Passive);
        if (driver is null) return NotFound("Driver participant not found.");

        var callerIsOrganizer = caller is Organizer;
        var callerIsDriver = caller.EventMemberId == driver.EventMemberId;
        if (!callerIsOrganizer && !callerIsDriver) return Forbid();

        var user = await _db.Users
            .AsNoTracking()
            .Where(u => u.UserId == userId.Value)
            .Select(u => new { u.FirstName, u.LastName, u.ProfilePictureUrl })
            .FirstOrDefaultAsync(ct);

        var displayName = user is null ? "Event member" : $"{user.FirstName} {user.LastName}".Trim();
        var roomName = $"event-{eventId}-driver-{driver.EventMemberId}";
        var identity = $"{eventId}:driver-call:{caller.EventMemberId}:{Guid.NewGuid():N}";
        var token = _liveKit.CreateJoinToken(roomName, identity, displayName, new
        {
            eventId,
            req.ActivityId,
            caller.EventMemberId,
            caller.UserId,
            driverParticipantId = driver.EventMemberId,
            role = callerIsDriver ? "Driver" : "Organizer",
            displayName,
            profilePictureUrl = user?.ProfilePictureUrl
        });

        return Ok(new
        {
            serverUrl = _liveKit.ServerUrl,
            token,
            roomName,
            identity,
            displayName,
            role = callerIsDriver ? "Driver" : "Organizer"
        });
    }

    private Guid? GetUserIdFromClaims()
    {
        var uid = User.FindFirstValue("uid");
        return Guid.TryParse(uid, out var userId) ? userId : null;
    }
}

public sealed class DriverCallRequest
{
    public Guid DriverParticipantId { get; set; }
    public Guid? ActivityId { get; set; }
}
