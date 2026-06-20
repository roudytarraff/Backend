using System.Collections.Concurrent;
using System.Security.Claims;
using Backend.Services.Billing;
using Backend.Services.Push;
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
    private static readonly ConcurrentDictionary<Guid, DriverCallState> ActiveDriverCalls = new();
    private static readonly TimeSpan DriverCallRingTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DriverCallActiveTtl = TimeSpan.FromHours(3);

    private readonly AppDbContext _db;
    private readonly IHubContext<Hubs.EventHub> _hubContext;
    private readonly LiveKitTokenService _liveKit;
    private readonly PlanLimitService _plans;
    private readonly PushNotificationService _push;

    public EventVoiceController(
        AppDbContext db,
        IHubContext<Hubs.EventHub> hubContext,
        LiveKitTokenService liveKit,
        PlanLimitService plans,
        PushNotificationService push)
    {
        _db = db;
        _hubContext = hubContext;
        _liveKit = liveKit;
        _plans = plans;
        _push = push;
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
        await _plans.EnsureEventVoiceAllowed(_db, eventId, ct);
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
        var identity = $"{eventId}:{member.EventMemberId}";
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
        await _plans.EnsureDriverCallsAllowed(_db, eventId, ct);
        var organizer = ev.Organizers.FirstOrDefault(o => o.UserId == userId.Value && o.Status == MembershipStatus.Active);
        if (organizer is null) return Forbid();

        var driver = FindActiveMember(ev, req.DriverParticipantId);
        if (driver is null) return NotFound("Driver not found.");
        if (driver is Organizer) return BadRequest("Organizer drivers use the event voice channel.");
        if (driver.EventMemberId == organizer.EventMemberId) return BadRequest("You cannot call yourself as the driver.");

        var callId = req.CallId ?? Guid.NewGuid();
        PruneExpiredCalls();
        RemoveExistingCalls(eventId, driver.EventMemberId, organizer.EventMemberId, req.ActivityId);
        var requestedAt = DateTime.UtcNow;
        var expiresAt = requestedAt.Add(DriverCallRingTtl);
        ActiveDriverCalls[callId] = new DriverCallState(
            callId,
            eventId,
            req.ActivityId,
            driver.EventMemberId,
            organizer.EventMemberId,
            requestedAt);

        await _hubContext.Clients.Group($"event-{eventId}")
            .SendAsync("DriverCallRequested", new
            {
                EventId = eventId,
                req.ActivityId,
                CallId = callId,
                DriverParticipantId = driver.EventMemberId,
                RequestedByMemberId = organizer.EventMemberId,
                RequestedAt = requestedAt,
                ExpiresAt = expiresAt
            }, ct);

        await _push.SendToEventMembersAsync(
            eventId,
            new[] { driver.EventMemberId },
            "Incoming driver call",
            $"Organizer is calling you for {ev.Title}.",
            new Dictionary<string, string>
            {
                ["type"] = "driver-call",
                ["eventId"] = eventId.ToString(),
                ["activityId"] = req.ActivityId?.ToString() ?? "",
                ["callId"] = callId.ToString(),
                ["driverParticipantId"] = driver.EventMemberId.ToString(),
                ["title"] = ev.Title,
                ["expiresAt"] = expiresAt.ToString("O")
            },
            ct);

        return Ok(new { callId });
    }

    [HttpPost("driver-call/respond")]
    public async Task<IActionResult> RespondDriverCall(Guid eventId, [FromBody] DriverCallResponse req, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();

        var ev = await _db.Events
            .AsNoTracking()
            .Include(e => e.Organizers)
            .Include(e => e.Participants)
            .FirstOrDefaultAsync(e => e.EventId == eventId, ct);

        if (ev is null) return NotFound("Event not found.");

        var driver = FindActiveMember(ev, req.DriverParticipantId);
        if (driver?.UserId != userId.Value) driver = null;
        if (driver is null) return Forbid();
        if (req.CallId is null) return BadRequest("Call id is required.");

        PruneExpiredCalls();
        if (!ActiveDriverCalls.TryGetValue(req.CallId.Value, out var call) ||
            call.EventId != eventId ||
            call.DriverParticipantId != driver.EventMemberId ||
            call.ActivityId != req.ActivityId)
        {
            return BadRequest("This driver call is no longer active.");
        }
        if (call.AcceptedAt is null && DateTime.UtcNow - call.RequestedAt > DriverCallRingTtl)
        {
            ActiveDriverCalls.TryRemove(req.CallId.Value, out _);
            return BadRequest("This driver call has expired.");
        }

        if (req.Accepted)
        {
            ActiveDriverCalls[req.CallId.Value] = call with { AcceptedAt = DateTime.UtcNow };
        }
        else
        {
            ActiveDriverCalls.TryRemove(req.CallId.Value, out _);
        }

        await _hubContext.Clients.Group($"event-{eventId}")
            .SendAsync("DriverCallAnswered", new
            {
                EventId = eventId,
                req.ActivityId,
                req.CallId,
                DriverParticipantId = driver.EventMemberId,
                Accepted = req.Accepted,
                AnsweredAt = DateTime.UtcNow
            }, ct);

        await _push.SendToEventMembersAsync(
            eventId,
            ev.Organizers
                .Where(o => o.Status == MembershipStatus.Active && o.EventMemberId == call.RequestedByMemberId)
                .Select(o => o.EventMemberId),
            req.Accepted ? "Driver answered" : "Driver declined",
            req.Accepted ? "The driver answered your call." : "The driver declined your call.",
            new Dictionary<string, string>
            {
                ["type"] = req.Accepted ? "driver-call-answered" : "driver-call-ended",
                ["eventId"] = eventId.ToString(),
                ["activityId"] = req.ActivityId?.ToString() ?? "",
                ["callId"] = req.CallId?.ToString() ?? "",
                ["driverParticipantId"] = driver.EventMemberId.ToString(),
                ["accepted"] = req.Accepted ? "true" : "false"
            },
            ct);

        return Ok();
    }

    [HttpPost("driver-call/status")]
    public async Task<IActionResult> DriverCallStatus(Guid eventId, [FromBody] DriverCallResponse req, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();
        if (req.CallId is null) return BadRequest("Call id is required.");

        var member = await _db.EventMembers
            .AsNoTracking()
            .Where(m => m.EventId == eventId && m.UserId == userId.Value && m.Status == MembershipStatus.Active)
            .Select(m => new { m.EventMemberId })
            .FirstOrDefaultAsync(ct);

        if (member is null) return Forbid();

        PruneExpiredCalls();
        if (!ActiveDriverCalls.TryGetValue(req.CallId.Value, out var call) ||
            call.EventId != eventId ||
            call.DriverParticipantId != req.DriverParticipantId ||
            call.ActivityId != req.ActivityId ||
            (member.EventMemberId != call.DriverParticipantId && member.EventMemberId != call.RequestedByMemberId))
        {
            return Ok(new { status = "ended" });
        }

        return Ok(new
        {
            status = call.AcceptedAt is null ? "pending" : "accepted",
            accepted = call.AcceptedAt is not null,
            callId = call.CallId,
            expiresAt = call.RequestedAt.Add(DriverCallRingTtl)
        });
    }

    [HttpPost("driver-call/cancel")]
    public async Task<IActionResult> CancelDriverCall(Guid eventId, [FromBody] DriverCallResponse req, CancellationToken ct)
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

        var driver = FindActiveMember(ev, req.DriverParticipantId);
        if (driver is null) return NotFound("Driver not found.");
        if (req.CallId is not null)
        {
            ActiveDriverCalls.TryRemove(req.CallId.Value, out _);
        }
        else
        {
            RemoveExistingCalls(eventId, driver.EventMemberId, organizer.EventMemberId, req.ActivityId);
        }

        await _hubContext.Clients.Group($"event-{eventId}")
            .SendAsync("DriverCallAnswered", new
            {
                EventId = eventId,
                req.ActivityId,
                req.CallId,
                DriverParticipantId = driver.EventMemberId,
                Accepted = false,
                CancelledByCaller = true,
                AnsweredAt = DateTime.UtcNow
            }, ct);

        await _push.SendToEventMembersAsync(
            eventId,
            new[] { driver.EventMemberId },
            "Driver call cancelled",
            "The organizer cancelled the driver call.",
            new Dictionary<string, string>
            {
                ["type"] = "driver-call-ended",
                ["eventId"] = eventId.ToString(),
                ["activityId"] = req.ActivityId?.ToString() ?? "",
                ["callId"] = req.CallId?.ToString() ?? "",
                ["driverParticipantId"] = driver.EventMemberId.ToString(),
                ["cancelledByCaller"] = "true"
            },
            ct);

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
        await _plans.EnsureDriverCallsAllowed(_db, eventId, ct);

        var caller = ev.Organizers.Cast<EventMember>().Concat(ev.Participants).FirstOrDefault(m =>
            m.UserId == userId.Value &&
            m.Status == MembershipStatus.Active);
        if (caller is null) return Forbid();

        var driver = FindActiveMember(ev, req.DriverParticipantId);
        if (driver is null) return NotFound("Driver not found.");
        if (driver is Organizer) return BadRequest("Organizer drivers use the event voice channel.");
        if (req.CallId is null) return BadRequest("Call id is required.");

        PruneExpiredCalls();
        if (!ActiveDriverCalls.TryGetValue(req.CallId.Value, out var call) ||
            call.EventId != eventId ||
            call.DriverParticipantId != driver.EventMemberId ||
            call.ActivityId != req.ActivityId ||
            call.AcceptedAt is null)
        {
            return BadRequest("This driver call is no longer active.");
        }

        var callerIsOrganizer = caller is Organizer;
        var callerIsDriver = caller.EventMemberId == driver.EventMemberId;
        if (!callerIsDriver && caller.EventMemberId != call.RequestedByMemberId) return Forbid();

        var user = await _db.Users
            .AsNoTracking()
            .Where(u => u.UserId == userId.Value)
            .Select(u => new { u.FirstName, u.LastName, u.ProfilePictureUrl })
            .FirstOrDefaultAsync(ct);

        var displayName = user is null ? "Event member" : $"{user.FirstName} {user.LastName}".Trim();
        var roomName = $"event-{eventId}-driver-{driver.EventMemberId}-{req.CallId}";
        var identity = $"{eventId}:driver-call:{req.CallId}:{caller.EventMemberId}:{driver.EventMemberId}";
        var token = _liveKit.CreateJoinToken(roomName, identity, displayName, new
        {
            eventId,
            req.ActivityId,
            req.CallId,
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

    private static EventMember? FindActiveMember(Event ev, Guid eventMemberId)
        => ev.Organizers.Cast<EventMember>()
            .Concat(ev.Participants)
            .FirstOrDefault(m => m.EventMemberId == eventMemberId && m.Status == MembershipStatus.Active);

    private static void PruneExpiredCalls()
    {
        var now = DateTime.UtcNow;
        foreach (var item in ActiveDriverCalls)
        {
            var call = item.Value;
            var age = now - (call.AcceptedAt ?? call.RequestedAt);
            if ((call.AcceptedAt is null && age > DriverCallRingTtl) ||
                (call.AcceptedAt is not null && age > DriverCallActiveTtl))
            {
                ActiveDriverCalls.TryRemove(item.Key, out _);
            }
        }
    }

    private static void RemoveExistingCalls(Guid eventId, Guid driverParticipantId, Guid requestedByMemberId, Guid? activityId)
    {
        foreach (var item in ActiveDriverCalls)
        {
            var call = item.Value;
            if (call.EventId == eventId &&
                call.DriverParticipantId == driverParticipantId &&
                call.RequestedByMemberId == requestedByMemberId &&
                call.ActivityId == activityId)
            {
                ActiveDriverCalls.TryRemove(item.Key, out _);
            }
        }
    }
}

internal sealed record DriverCallState(
    Guid CallId,
    Guid EventId,
    Guid? ActivityId,
    Guid DriverParticipantId,
    Guid RequestedByMemberId,
    DateTime RequestedAt)
{
    public DateTime? AcceptedAt { get; init; }
}

public sealed class DriverCallRequest
{
    public Guid DriverParticipantId { get; set; }
    public Guid? ActivityId { get; set; }
    public Guid? CallId { get; set; }
}

public sealed class DriverCallResponse
{
    public Guid DriverParticipantId { get; set; }
    public Guid? ActivityId { get; set; }
    public Guid? CallId { get; set; }
    public bool Accepted { get; set; }
}
