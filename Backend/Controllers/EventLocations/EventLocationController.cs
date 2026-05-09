using System.Security.Claims;
using Backend.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TripPlanner.Api.Data;
using TripPlanner.Api.Domain;
using TripPlanner.Api.Domain.Events;
using TripPlanner.Api.Domain.Location;

namespace Backend.Controllers.EventLocations;

[ApiController]
[Route("api/events/{eventId:guid}/locations")]
[Authorize]
public sealed class EventLocationController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHubContext<EventHub> _hubContext;

    public EventLocationController(AppDbContext db, IHubContext<EventHub> hubContext)
    {
        _db = db;
        _hubContext = hubContext;
    }

    [HttpPost("me")]
    public async Task<IActionResult> UpdateMyLocation(Guid eventId, [FromBody] UpdateMyEventLocationRequest req, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();

        if (!IsValidCoordinate(req.Latitude, req.Longitude))
        {
            return BadRequest("A valid latitude and longitude are required.");
        }

        var member = await _db.EventMembers
            .Include(m => m.LocationSession)
            .FirstOrDefaultAsync(m =>
                m.EventId == eventId &&
                m.UserId == userId.Value &&
                m.Status == MembershipStatus.Active,
                ct);

        if (member is null)
        {
            var eventExists = await _db.Events
                .AsNoTracking()
                .AnyAsync(e => e.EventId == eventId, ct);

            return eventExists ? Forbid() : NotFound("Event not found.");
        }

        var session = member.LocationSession;
        if (session is null)
        {
            session = new LocationSession(member.EventMemberId);
            _db.LocationSessions.Add(session);
        }
        else
        {
            session.Start();
        }

        var point = new LocationPoint(session.LocationSessionId, req.Latitude, req.Longitude, req.Accuracy);
        _db.LocationPoints.Add(point);

        await _db.SaveChangesAsync(ct);
        await TrimLocationPoints(session.LocationSessionId, ct);
        Console.WriteLine($"LOCATION POINT SAVED event={eventId} member={member.EventMemberId} lat={req.Latitude} lng={req.Longitude} point={point.LocationPointId}");

        await _hubContext.Clients.Group($"event-{eventId}").SendAsync("MemberLocationUpdated", new
        {
            EventId = eventId,
            member.EventMemberId,
            member.UserId,
            req.Latitude,
            req.Longitude,
            req.Accuracy,
            point.RecordedAt
        }, ct);

        return Ok(new
        {
            session.LocationSessionId,
            point.LocationPointId,
            point.RecordedAt
        });
    }

    private Guid? GetUserIdFromClaims()
    {
        var uid = User.FindFirstValue("uid");
        return Guid.TryParse(uid, out var userId) ? userId : null;
    }

    private static bool IsValidCoordinate(double latitude, double longitude)
        => latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;

    private async Task TrimLocationPoints(Guid locationSessionId, CancellationToken ct)
    {
        var extraPointIds = await _db.LocationPoints
            .Where(p => p.LocationSessionId == locationSessionId)
            .OrderByDescending(p => p.RecordedAt)
            .Skip(LocationSession.MaxPoints)
            .Select(p => p.LocationPointId)
            .ToListAsync(ct);

        if (extraPointIds.Count == 0) return;

        await _db.LocationPoints
            .Where(p => extraPointIds.Contains(p.LocationPointId))
            .ExecuteDeleteAsync(ct);
    }
}

public sealed class UpdateMyEventLocationRequest
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Accuracy { get; set; }
}
