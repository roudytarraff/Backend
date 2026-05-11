using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TripPlanner.Api.Data;
using TripPlanner.Api.Domain;
using TripPlanner.Api.Domain.Events;
using TripPlanner.Api.Domain.Location;

namespace Backend.Hubs;

[Authorize]
public sealed class EventHub : Hub
{
    private readonly AppDbContext _db;

    public EventHub(AppDbContext db)
    {
        _db = db;
    }

    public async Task JoinEventChannel(Guid eventId)
    {
        var ev = await _db.Events
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.EventId == eventId);

        if (ev is null)
        {
            await Clients.Caller.SendAsync("Error", "Event not found");
            return;
        }

        var groupName = $"event-{eventId}";
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        
        await Clients.Group(groupName).SendAsync("UserJoined", new
        {
            connectionId = Context.ConnectionId,
            timestamp = DateTime.UtcNow
        });
    }

    public async Task LeaveEventChannel(Guid eventId)
    {
        var groupName = $"event-{eventId}";
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);

        await Clients.Group(groupName).SendAsync("UserLeft", new
        {
            connectionId = Context.ConnectionId,
            timestamp = DateTime.UtcNow
        });
    }

    public async Task PublishMyLocation(Guid eventId, LiveLocationUpdateRequest req)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null)
        {
            throw new HubException("Authentication is required to share location.");
        }

        if (!IsValidCoordinate(req.Latitude, req.Longitude))
        {
            throw new HubException("A valid latitude and longitude are required.");
        }

        var member = await _db.EventMembers
            .Include(m => m.LocationSession)
            .FirstOrDefaultAsync(m =>
                m.EventId == eventId &&
                m.UserId == userId.Value &&
                m.Status == MembershipStatus.Active);

        if (member is null)
        {
            throw new HubException("You are not an active member of this event.");
        }

        var recordedAt = DateTime.UtcNow;
        var groupName = $"event-{eventId}";
        await Clients.Group(groupName).SendAsync("MemberLocationUpdated", new
        {
            EventId = eventId,
            member.EventMemberId,
            member.UserId,
            req.Latitude,
            req.Longitude,
            req.Accuracy,
            RecordedAt = recordedAt
        });

        try
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

            var point = new LocationPoint(session.LocationSessionId, req.Latitude, req.Longitude, req.Accuracy);
            _db.LocationPoints.Add(point);
            await _db.SaveChangesAsync();
            await TrimLocationPoints(session.LocationSessionId);
        }
        catch (Exception ex)
        {
            throw new HubException($"Location persistence failed: {ex.Message}");
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }

    private Guid? GetUserIdFromClaims()
    {
        var uid = Context.User?.FindFirstValue("uid");
        return Guid.TryParse(uid, out var userId) ? userId : null;
    }

    private static bool IsValidCoordinate(double latitude, double longitude)
        => latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;

    private async Task TrimLocationPoints(Guid locationSessionId)
    {
        var extraPointIds = await _db.LocationPoints
            .Where(p => p.LocationSessionId == locationSessionId)
            .OrderByDescending(p => p.RecordedAt)
            .Skip(LocationSession.MaxPoints)
            .Select(p => p.LocationPointId)
            .ToListAsync();

        if (extraPointIds.Count == 0) return;

        await _db.LocationPoints
            .Where(p => extraPointIds.Contains(p.LocationPointId))
            .ExecuteDeleteAsync();
    }
}

public sealed class LiveLocationUpdateRequest
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Accuracy { get; set; }
}
