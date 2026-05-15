using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TripPlanner.Api.Data;
using TripPlanner.Api.Domain;
using TripPlanner.Api.Domain.Events;

namespace Backend.Controllers.EventLocations;

[ApiController]
[Route("api/event-locations")]
[Authorize]
public sealed class ActiveLocationEventsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ActiveLocationEventsController(AppDbContext db)
    {
        _db = db;
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

    private Guid? GetUserIdFromClaims()
    {
        var uid = User.FindFirstValue("uid");
        return Guid.TryParse(uid, out var userId) ? userId : null;
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
