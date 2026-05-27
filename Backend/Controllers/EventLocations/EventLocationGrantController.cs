using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TripPlanner.Api.Data;
using TripPlanner.Api.Domain;
using TripPlanner.Api.Domain.Events;

namespace Backend.Controllers.EventLocations;

[ApiController]
[Authorize]
[Route("api/events/{eventId:guid}/location-grants")]
public sealed class EventLocationGrantController : ControllerBase
{
    private readonly AppDbContext _db;

    public EventLocationGrantController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> List(Guid eventId, CancellationToken ct)
    {
        var loaded = await LoadEventAndMember(eventId, ct);
        if (loaded.Result is not null)
            return loaded.Result;

        var (ev, currentMember) = loaded.Value;
        var members = ev.Organizers.Cast<EventMember>()
            .Concat(ev.Participants)
            .Where(m => m.Status == MembershipStatus.Active)
            .ToList();

        var users = await _db.Users
            .AsNoTracking()
            .Where(u => members.Select(m => m.UserId).Contains(u.UserId))
            .ToDictionaryAsync(u => u.UserId, ct);

        return Ok(new LocationGrantListDto
        {
            EventId = ev.EventId,
            CurrentMemberId = currentMember.EventMemberId,
            Members = members
                .Where(m => m.EventMemberId != currentMember.EventMemberId)
                .OrderBy(m => m is Organizer ? 0 : 1)
                .ThenBy(m => users.TryGetValue(m.UserId, out var u) ? u.FirstName : "")
                .Select(m => ToDto(ev, currentMember, m, users))
                .ToList()
        });
    }

    [HttpPost("{targetMemberId:guid}/request")]
    public async Task<IActionResult> RequestLocation(Guid eventId, Guid targetMemberId, CancellationToken ct)
    {
        var loaded = await LoadEventAndMember(eventId, ct);
        if (loaded.Result is not null)
            return loaded.Result;

        var (ev, currentMember) = loaded.Value;
        var target = FindActiveMember(ev, targetMemberId);
        if (target is null)
            return NotFound(new { message = "Member not found." });
        if (target.EventMemberId == currentMember.EventMemberId)
            return BadRequest(new { message = "You cannot request your own location." });

        var existing = ev.LocationGrants.FirstOrDefault(g =>
            g.GrantedByMemberId == target.EventMemberId &&
            g.GrantedToMemberId == currentMember.EventMemberId);

        if (existing is null)
        {
            ev.LocationGrants.Add(new LocationGrant(target.EventMemberId, currentMember.EventMemberId, LocationGrantStatus.Pending));
        }
        else if (!existing.IsActive)
        {
            existing.MarkPending();
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "Location request sent." });
    }

    [HttpPost("{requesterMemberId:guid}/confirm")]
    public async Task<IActionResult> ConfirmRequest(Guid eventId, Guid requesterMemberId, CancellationToken ct)
    {
        var loaded = await LoadEventAndMember(eventId, ct);
        if (loaded.Result is not null)
            return loaded.Result;

        var (ev, currentMember) = loaded.Value;
        var requester = FindActiveMember(ev, requesterMemberId);
        if (requester is null)
            return NotFound(new { message = "Member not found." });

        var request = ev.LocationGrants.FirstOrDefault(g =>
            g.GrantedByMemberId == currentMember.EventMemberId &&
            g.GrantedToMemberId == requester.EventMemberId &&
            g.Status == LocationGrantStatus.Pending);

        if (request is null)
            return NotFound(new { message = "Location request not found." });

        request.Activate();
        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "Location sharing enabled." });
    }

    [HttpDelete("{otherMemberId:guid}")]
    public async Task<IActionResult> Remove(Guid eventId, Guid otherMemberId, CancellationToken ct)
    {
        var loaded = await LoadEventAndMember(eventId, ct);
        if (loaded.Result is not null)
            return loaded.Result;

        var (ev, currentMember) = loaded.Value;
        var other = FindActiveMember(ev, otherMemberId);
        if (other is null)
            return NotFound(new { message = "Member not found." });

        ev.LocationGrants.RemoveAll(g =>
            (g.GrantedByMemberId == currentMember.EventMemberId && g.GrantedToMemberId == other.EventMemberId) ||
            (g.GrantedByMemberId == other.EventMemberId && g.GrantedToMemberId == currentMember.EventMemberId));

        await _db.SaveChangesAsync(ct);
        return Ok(new { message = "Location sharing removed." });
    }

    private async Task<ActionLoadResult> LoadEventAndMember(Guid eventId, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null)
            return new ActionLoadResult(Unauthorized(), null, null);

        var ev = await _db.Events
            .Include(e => e.Organizers)
            .Include(e => e.Participants)
            .Include(e => e.LocationGrants)
            .FirstOrDefaultAsync(e => e.EventId == eventId, ct);

        if (ev is null)
            return new ActionLoadResult(NotFound(new { message = "Event not found." }), null, null);

        var member = ev.Organizers.Cast<EventMember>()
            .Concat(ev.Participants)
            .FirstOrDefault(m => m.UserId == userId.Value && m.Status == MembershipStatus.Active);

        return member is null
            ? new ActionLoadResult(Forbid(), null, null)
            : new ActionLoadResult(null, ev, member);
    }

    private static EventMember? FindActiveMember(Event ev, Guid eventMemberId)
        => ev.Organizers.Cast<EventMember>()
            .Concat(ev.Participants)
            .FirstOrDefault(m => m.EventMemberId == eventMemberId && m.Status == MembershipStatus.Active);

    private static LocationGrantMemberDto ToDto(
        Event ev,
        EventMember currentMember,
        EventMember member,
        Dictionary<Guid, TripPlanner.Api.Domain.Users.User> users)
    {
        users.TryGetValue(member.UserId, out var user);
        var grantedToMe = ev.LocationGrants.FirstOrDefault(g =>
            g.GrantedByMemberId == member.EventMemberId &&
            g.GrantedToMemberId == currentMember.EventMemberId);
        var grantedByMe = ev.LocationGrants.FirstOrDefault(g =>
            g.GrantedByMemberId == currentMember.EventMemberId &&
            g.GrantedToMemberId == member.EventMemberId);

        return new LocationGrantMemberDto
        {
            EventMemberId = member.EventMemberId,
            UserId = member.UserId,
            FullName = user is null ? "Unknown User" : $"{user.FirstName} {user.LastName}",
            ProfilePictureUrl = user?.ProfilePictureUrl,
            Role = member.EventMemberId == ev.OwnerOrganizerId ? "Owner" : member is Organizer ? "Organizer" : "Participant",
            IsPassiveParticipant = member is Participant p && p.Mode == ParticipantMode.Passive,
            CanSeeTheirLocation = grantedToMe is { IsActive: true, Status: LocationGrantStatus.Active },
            TheyCanSeeMyLocation = grantedByMe is { IsActive: true, Status: LocationGrantStatus.Active },
            RequestSent = grantedToMe is { Status: LocationGrantStatus.Pending },
            RequestReceived = grantedByMe is { Status: LocationGrantStatus.Pending }
        };
    }

    private Guid? GetUserIdFromClaims()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? User.FindFirst("sub")?.Value
                    ?? User.FindFirst("userId")?.Value;
        return Guid.TryParse(claim, out var userId) ? userId : null;
    }

    private sealed record ActionLoadResult(IActionResult? Result, Event? Event, EventMember? Member)
    {
        public (Event Event, EventMember Member) Value => (Event!, Member!);
    }
}

public sealed class LocationGrantListDto
{
    public Guid EventId { get; set; }
    public Guid CurrentMemberId { get; set; }
    public List<LocationGrantMemberDto> Members { get; set; } = new();
}

public sealed class LocationGrantMemberDto
{
    public Guid EventMemberId { get; set; }
    public Guid UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? ProfilePictureUrl { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsPassiveParticipant { get; set; }
    public bool CanSeeTheirLocation { get; set; }
    public bool TheyCanSeeMyLocation { get; set; }
    public bool RequestSent { get; set; }
    public bool RequestReceived { get; set; }
}
