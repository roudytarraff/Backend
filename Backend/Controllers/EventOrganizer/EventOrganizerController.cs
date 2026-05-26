using System.Security.Claims;
using Backend.Services.Billing;
using Backend.Services.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TripPlanner.Api.Data;
using TripPlanner.Api.Domain;
using TripPlanner.Api.Domain.Events;

namespace Backend.Controllers.EventOrganizer;

[ApiController]
[Route("api/events/{eventId:guid}/organizer")]
[Authorize]
public sealed class EventOrganizerController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IBlobStorageService _blobStorage;
    private readonly IHubContext<Hubs.EventHub> _hubContext;
    private readonly PlanLimitService _plans;

    public EventOrganizerController(
        AppDbContext db,
        IBlobStorageService blobStorage,
        IHubContext<Hubs.EventHub> hubContext,
        PlanLimitService plans)
    {
        _db = db;
        _blobStorage = blobStorage;
        _hubContext = hubContext;
        _plans = plans;
    }

    [HttpPost("start")]
    public async Task<IActionResult> Start(Guid eventId, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();

        var ev = await LoadEventWithMembers(eventId, ct);
        if (ev is null) return NotFound("Event not found.");

        if (ev.Status == EventStatus.Draft)
        {
            var owner = ev.Organizers.FirstOrDefault(o => o.EventMemberId == ev.OwnerOrganizerId);
            if (owner is null) return BadRequest("Event owner was not found.");
            await _plans.EnsureCanPublishEvent(_db, owner.UserId, ct);
        }

        ev.Start(userId.Value);
        await _db.SaveChangesAsync(ct);
        await BroadcastDetailsChanged(eventId, "EventStarted");

        return Ok(new { ev.EventId, ev.Status });
    }

    [HttpPost("end")]
    public async Task<IActionResult> End(Guid eventId, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();

        var ev = await LoadEventWithMembers(eventId, ct);
        if (ev is null) return NotFound("Event not found.");

        ev.End(userId.Value);
        await _db.SaveChangesAsync(ct);
        await BroadcastDetailsChanged(eventId, "EventEnded");

        return Ok(new { ev.EventId, ev.Status });
    }

    [HttpPut("details")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UpdateDetails(Guid eventId, [FromForm] UpdateOrganizerEventRequest req, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();

        var ev = await LoadEventWithMembers(eventId, ct);
        if (ev is null) return NotFound("Event not found.");
        if (!IsActiveOrganizer(ev, userId.Value)) return Forbid();

        if (!string.IsNullOrWhiteSpace(req.Title))
        {
            ev.UpdateDetails(userId.Value, req.Title, req.Description ?? ev.Description ?? "", req.EventType ?? ev.EventType);
        }

        if (req.CoverImage is not null)
        {
            var url = await _blobStorage.UploadImageAsync(req.CoverImage, "events", ct);
            ev.UpdateThumbnail(userId.Value, url);
        }

        await _db.SaveChangesAsync(ct);
        await BroadcastDetailsChanged(eventId, "EventDetailsUpdated");

        return Ok(new
        {
            ev.EventId,
            ev.Title,
            ev.Description,
            ev.EventType,
            ev.ThumbnailUrl
        });
    }

    [HttpGet("members")]
    public async Task<IActionResult> Members(Guid eventId, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();

        var ev = await LoadEventWithMembers(eventId, ct);
        if (ev is null) return NotFound("Event not found.");
        if (!IsActiveOrganizer(ev, userId.Value)) return Forbid();

        var memberUserIds = ev.Organizers
            .Cast<EventMember>()
            .Concat(ev.Participants)
            .Where(m => m.Status == MembershipStatus.Active)
            .Select(m => m.UserId)
            .Distinct()
            .ToList();

        var users = await _db.Users
            .AsNoTracking()
            .Where(u => memberUserIds.Contains(u.UserId))
            .ToDictionaryAsync(u => u.UserId, ct);

        var members = ev.Organizers
            .Cast<EventMember>()
            .Concat(ev.Participants)
            .Where(m => m.Status == MembershipStatus.Active)
            .Select(m =>
            {
                users.TryGetValue(m.UserId, out var user);
                return new EventMemberDto
                {
                    EventMemberId = m.EventMemberId,
                    UserId = m.UserId,
                    FullName = user is null ? "Unknown User" : $"{user.FirstName} {user.LastName}",
                    Email = user?.Email ?? "",
                    ProfilePictureUrl = user?.ProfilePictureUrl,
                    Role = m.EventMemberId == ev.OwnerOrganizerId ? "Owner" : m is Organizer ? "Organizer" : "Participant",
                    ParticipantMode = m is Participant p ? p.Mode : null,
                    JoinedAt = m.JoinedAt
                };
            })
            .OrderBy(m => m.Role == "Owner" ? 0 : m.Role == "Organizer" ? 1 : 2)
            .ThenBy(m => m.FullName)
            .ToList();

        return Ok(members);
    }

    [HttpPost("members/{memberId:guid}/promote")]
    public async Task<IActionResult> Promote(Guid eventId, Guid memberId, CancellationToken ct)
    {
        var actorUserId = GetUserIdFromClaims();
        if (actorUserId is null) return Unauthorized();

        var ev = await LoadEventWithMembers(eventId, ct);
        if (ev is null) return NotFound("Event not found.");
        if (!IsActiveOrganizer(ev, actorUserId.Value)) return Forbid();

        var participant = ev.Participants.FirstOrDefault(p => p.EventMemberId == memberId && p.Status == MembershipStatus.Active);
        if (participant is null) return NotFound("Participant not found.");

        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [EventMembers]
            SET [MemberType] = N'Organizer'
            WHERE [EventMemberId] = {memberId} AND [EventId] = {eventId} AND [Status] = {(int)MembershipStatus.Active}
            """, ct);
        await BroadcastDetailsChanged(eventId, "MemberPromoted");

        return Ok();
    }

    [HttpPost("members/{memberId:guid}/demote")]
    public async Task<IActionResult> Demote(Guid eventId, Guid memberId, CancellationToken ct)
    {
        var actorUserId = GetUserIdFromClaims();
        if (actorUserId is null) return Unauthorized();

        var ev = await LoadEventWithMembers(eventId, ct);
        if (ev is null) return NotFound("Event not found.");
        if (!IsOwner(ev, actorUserId.Value)) return Forbid();
        if (memberId == ev.OwnerOrganizerId) return BadRequest("Cannot demote the owner.");

        var organizer = ev.Organizers.FirstOrDefault(o => o.EventMemberId == memberId && o.Status == MembershipStatus.Active);
        if (organizer is null) return NotFound("Organizer not found.");

        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [EventMembers]
            SET [MemberType] = N'Participant', [Mode] = {(int)ParticipantMode.Active}
            WHERE [EventMemberId] = {memberId} AND [EventId] = {eventId} AND [Status] = {(int)MembershipStatus.Active}
            """, ct);
        await BroadcastDetailsChanged(eventId, "MemberDemoted");

        return Ok();
    }

    [HttpDelete("members/{memberId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid eventId, Guid memberId, CancellationToken ct)
    {
        var actorUserId = GetUserIdFromClaims();
        if (actorUserId is null) return Unauthorized();

        var ev = await LoadEventWithMembers(eventId, ct);
        if (ev is null) return NotFound("Event not found.");
        if (!IsActiveOrganizer(ev, actorUserId.Value)) return Forbid();
        if (memberId == ev.OwnerOrganizerId) return BadRequest("Cannot remove the owner.");

        var organizer = ev.Organizers.FirstOrDefault(o => o.EventMemberId == memberId);
        if (organizer is not null)
        {
            organizer.Remove();
        }
        else
        {
            var participant = ev.Participants.FirstOrDefault(p => p.EventMemberId == memberId);
            if (participant is null) return NotFound("Member not found.");
            participant.Remove();
        }

        await UnassignDriverFromActivities(eventId, memberId, ct);
        await _db.SaveChangesAsync(ct);
        await BroadcastDetailsChanged(eventId, "MemberRemoved");

        return Ok();
    }

    private Task<Event?> LoadEventWithMembers(Guid eventId, CancellationToken ct)
        => _db.Events
            .Include(e => e.Organizers)
            .Include(e => e.Participants)
            .FirstOrDefaultAsync(e => e.EventId == eventId, ct);

    private static bool IsActiveOrganizer(Event ev, Guid userId)
        => ev.Organizers.Any(o => o.UserId == userId && o.Status == MembershipStatus.Active);

    private static bool IsOwner(Event ev, Guid userId)
        => ev.Organizers.Any(o => o.EventMemberId == ev.OwnerOrganizerId && o.UserId == userId && o.Status == MembershipStatus.Active);

    private Task UnassignDriverFromActivities(Guid eventId, Guid memberId, CancellationToken ct)
        => _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE [Activities]
            SET [DriverParticipantId] = NULL, [DriverDisplayName] = NULL
            WHERE [DriverParticipantId] = {memberId}
              AND [EventDayId] IN (
                  SELECT [EventDayId]
                  FROM [EventDays]
                  WHERE [EventId] = {eventId}
              )
            """, ct);

    private Task BroadcastDetailsChanged(Guid eventId, string reason = "EventDetailsUpdated")
        => _hubContext.Clients.Group($"event-{eventId}").SendAsync("EventDetailsUpdated", new
        {
            EventId = eventId,
            Reason = reason,
            UpdatedAt = DateTime.UtcNow
        });

    private Guid? GetUserIdFromClaims()
    {
        var uid = User.FindFirstValue("uid");
        return Guid.TryParse(uid, out var userId) ? userId : null;
    }
}

public sealed class UpdateOrganizerEventRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? EventType { get; set; }
    public IFormFile? CoverImage { get; set; }
}

public sealed class EventMemberDto
{
    public Guid EventMemberId { get; set; }
    public Guid UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? ProfilePictureUrl { get; set; }
    public string Role { get; set; } = string.Empty;
    public ParticipantMode? ParticipantMode { get; set; }
    public DateTime JoinedAt { get; set; }
}
