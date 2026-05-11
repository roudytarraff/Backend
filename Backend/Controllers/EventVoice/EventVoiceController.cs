using System.Security.Claims;
using Backend.Services.Voice;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
            .Where(m => m.EventId == eventId && m.UserId == userId.Value && m.Status == MembershipStatus.Active)
            .Select(m => new
            {
                m.EventMemberId,
                m.UserId,
                Role = EF.Property<string>(m, "MemberType")
            })
            .FirstOrDefaultAsync(ct);

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

        var user = await _db.Users
            .AsNoTracking()
            .Where(u => u.UserId == userId.Value)
            .Select(u => new { u.FirstName, u.LastName, u.ProfilePictureUrl })
            .FirstOrDefaultAsync(ct);

        var role = ev.OwnerOrganizerId == member.EventMemberId ? "Owner" : member.Role;
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

    private Guid? GetUserIdFromClaims()
    {
        var uid = User.FindFirstValue("uid");
        return Guid.TryParse(uid, out var userId) ? userId : null;
    }
}
