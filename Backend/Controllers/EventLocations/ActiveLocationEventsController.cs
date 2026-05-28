using System.Security.Claims;
using System.Text.Json;
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
[Route("api/event-locations")]
[Authorize]
public sealed class ActiveLocationEventsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHubContext<EventHub> _hubContext;

    public ActiveLocationEventsController(AppDbContext db, IHubContext<EventHub> hubContext)
    {
        _db = db;
        _hubContext = hubContext;
    }

    [HttpGet("active")]
    public async Task<IActionResult> Active(CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();

        var events = await (
            from member in _db.EventMembers.AsNoTracking()
            join ev in _db.Events.AsNoTracking() on member.EventId equals ev.EventId
            where member.UserId == userId.Value &&
                  member.Status == MembershipStatus.Active &&
                  ev.Status == EventStatus.Active
            select new ActiveLocationEventDto
            {
                EventId = member.EventId,
                EventMemberId = member.EventMemberId,
                Title = ev.Title,
                Role = ev.OwnerOrganizerId == member.EventMemberId
                    ? "Owner"
                    : EF.Property<string>(member, "MemberType"),
                IsLocationSharingActive = true
            })
            .ToListAsync(ct);

        return Ok(events);
    }

    [HttpPost("background/me")]
    public async Task<IActionResult> UpdateMyBackgroundLocation([FromBody] JsonElement body, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();

        if (!TryReadLocation(body, out var latitude, out var longitude, out var accuracy))
        {
            return BadRequest("A valid latitude and longitude are required.");
        }

        var memberships = await (
            from member in _db.EventMembers.Include(m => m.LocationSession)
            join ev in _db.Events on member.EventId equals ev.EventId
            where member.UserId == userId.Value &&
                  member.Status == MembershipStatus.Active &&
                  ev.Status == EventStatus.Active
            select member)
            .ToListAsync(ct);

        if (memberships.Count == 0) return Ok(new { updated = 0 });

        var updates = new List<BackgroundLocationUpdate>();
        foreach (var member in memberships)
        {
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

            var point = new LocationPoint(session.LocationSessionId, latitude, longitude, accuracy);
            _db.LocationPoints.Add(point);
            updates.Add(new BackgroundLocationUpdate(
                member.EventId,
                member.EventMemberId,
                member.UserId,
                latitude,
                longitude,
                accuracy,
                point.RecordedAt,
                session.LocationSessionId));
        }

        await _db.SaveChangesAsync(ct);

        foreach (var sessionId in updates.Select(u => u.SessionId).Distinct())
        {
            await TrimLocationPoints(sessionId, ct);
        }

        foreach (var update in updates)
        {
            await _hubContext.Clients.Group($"event-{update.EventId}").SendAsync("MemberLocationUpdated", new
            {
                update.EventId,
                update.EventMemberId,
                update.UserId,
                update.Latitude,
                update.Longitude,
                update.Accuracy,
                update.RecordedAt
            }, ct);
        }

        return Ok(new { updated = updates.Count });
    }

    private Guid? GetUserIdFromClaims()
    {
        var uid = User.FindFirstValue("uid");
        return Guid.TryParse(uid, out var userId) ? userId : null;
    }

    private static bool TryReadLocation(JsonElement body, out double latitude, out double longitude, out double accuracy)
    {
        accuracy = 0;
        if (TryReadCoordinate(body, out latitude, out longitude, out accuracy)) return true;

        if (body.ValueKind == JsonValueKind.Object &&
            body.TryGetProperty("coords", out var coords) &&
            TryReadCoordinate(coords, out latitude, out longitude, out accuracy))
        {
            return true;
        }

        if (body.ValueKind == JsonValueKind.Object &&
            body.TryGetProperty("location", out var location))
        {
            if (TryReadCoordinate(location, out latitude, out longitude, out accuracy)) return true;
            if (location.ValueKind == JsonValueKind.Object &&
                location.TryGetProperty("coords", out var nestedCoords) &&
                TryReadCoordinate(nestedCoords, out latitude, out longitude, out accuracy))
            {
                return true;
            }
        }

        latitude = 0;
        longitude = 0;
        accuracy = 0;
        return false;
    }

    private static bool TryReadCoordinate(JsonElement element, out double latitude, out double longitude, out double accuracy)
    {
        latitude = 0;
        longitude = 0;
        accuracy = 0;

        if (element.ValueKind != JsonValueKind.Object) return false;
        if (!TryGetDouble(element, "latitude", out latitude)) return false;
        if (!TryGetDouble(element, "longitude", out longitude)) return false;
        TryGetDouble(element, "accuracy", out accuracy);

        return latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;
    }

    private static bool TryGetDouble(JsonElement element, string propertyName, out double value)
    {
        value = 0;
        if (!element.TryGetProperty(propertyName, out var property)) return false;

        if (property.ValueKind == JsonValueKind.Number) return property.TryGetDouble(out value);
        if (property.ValueKind == JsonValueKind.String) return double.TryParse(property.GetString(), out value);
        return false;
    }

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

public sealed class ActiveLocationEventDto
{
    public Guid EventId { get; set; }
    public Guid EventMemberId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsLocationSharingActive { get; set; }
}

internal sealed record BackgroundLocationUpdate(
    Guid EventId,
    Guid EventMemberId,
    Guid UserId,
    double Latitude,
    double Longitude,
    double Accuracy,
    DateTime RecordedAt,
    Guid SessionId);
