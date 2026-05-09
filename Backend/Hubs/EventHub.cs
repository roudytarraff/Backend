using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TripPlanner.Api.Data;
using TripPlanner.Api.Domain.Events;

namespace Backend.Hubs;

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

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }
}
