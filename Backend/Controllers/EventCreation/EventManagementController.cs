using System.Security.Claims;
using Backend.Services.Crypto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TripPlanner.Api.Data;
using TripPlanner.Api.Domain;
using TripPlanner.Api.Domain.Events;

namespace Backend.Controllers.EventCreation;

[ApiController]
[Route("api/events")]
[Authorize]
public sealed class EventManagementController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHubContext<Hubs.EventHub> _hubContext;
    private readonly IJoinPasswordCryptoService _crypto;

    public EventManagementController(
        AppDbContext db,
        IHubContext<Hubs.EventHub> hubContext,
        IJoinPasswordCryptoService crypto)
    {
        _db = db;
        _hubContext = hubContext;
        _crypto = crypto;
    }

    [HttpGet]
    public async Task<IActionResult> ListEvents(CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId == null)
            return Unauthorized();

        var events = await _db.Events
            .AsNoTracking()
            .Where(e => e.Organizers.Any(o => o.UserId == userId) ||
                       e.Participants.Any(p => p.UserId == userId))
            .Select(e => new EventListItemDto
            {
                EventId = e.EventId,
                Title = e.Title,
                EventType = e.EventType,
                StartDate = e.StartDate,
                EndDate = e.EndDate,
                Status = e.Status,
                DestinationName = e.DestinationName,
                ThumbnailUrl = e.ThumbnailUrl,
                CreatedAt = e.CreatedAt
            })
            .ToListAsync(ct);

        return Ok(events);
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetUserEvents(CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId == null)
            return Unauthorized();

        var events = await _db.Events
            .AsNoTracking()
            .Where(e => e.Organizers.Any(o => o.UserId == userId && o.Status == MembershipStatus.Active) ||
                       e.Participants.Any(p => p.UserId == userId && p.Status == MembershipStatus.Active))
            .Select(e => new UserEventDto
            {
                EventId = e.EventId,
                Title = e.Title,
                EventType = e.EventType,
                StartDate = e.StartDate,
                EndDate = e.EndDate,
                Status = e.Status,
                DestinationName = e.DestinationName,
                ThumbnailUrl = e.ThumbnailUrl,
                CreatedAt = e.CreatedAt,
                EventMemberId = e.Organizers
                    .Where(o => o.UserId == userId && o.Status == MembershipStatus.Active)
                    .Select(o => (Guid?)o.EventMemberId)
                    .FirstOrDefault() ?? e.Participants
                    .Where(p => p.UserId == userId && p.Status == MembershipStatus.Active)
                    .Select(p => (Guid?)p.EventMemberId)
                    .FirstOrDefault(),
                Role = e.Organizers.Any(o => o.UserId == userId && o.EventMemberId == e.OwnerOrganizerId && o.Status == MembershipStatus.Active) ? "Owner" :
                       e.Organizers.Any(o => o.UserId == userId && o.Status == MembershipStatus.Active) ? "Organizer" :
                       e.Participants.Any(p => p.UserId == userId && p.Status == MembershipStatus.Active) ? "Participant" : "Unknown",
                ParticipantMode = e.Participants
                    .Where(p => p.UserId == userId && p.Status == MembershipStatus.Active)
                    .Select(p => (ParticipantMode?)p.Mode)
                    .FirstOrDefault()
            })
            .OrderByDescending(e => e.StartDate)
            .ToListAsync(ct);

        return Ok(events);
    }

    [HttpGet("details/{eventId}")]
    public async Task<IActionResult> GetEventDetails(Guid eventId, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId == null)
            return Unauthorized();

        var ev = await _db.Events
            .AsNoTracking()
            .AsSplitQuery()
            .Include(e => e.Organizers)
            .Include(e => e.Participants)
            .Include(e => e.EventDays)
                .ThenInclude(d => d.Activities)
                    .ThenInclude(a => a.Steps)
            .FirstOrDefaultAsync(e => e.EventId == eventId, ct);

        if (ev == null)
            return NotFound("Event not found.");

        // Check if user is member
        var isMember = ev.Organizers.Any(o => o.UserId == userId && o.Status == MembershipStatus.Active) ||
                      ev.Participants.Any(p => p.UserId == userId && p.Status == MembershipStatus.Active);

        if (!isMember)
            return Forbid();

        return Ok(ToDetailsDto(ev, userId.Value));
    }

    [HttpPut("{eventId}")]
    public async Task<IActionResult> UpdateEvent(Guid eventId, [FromBody] UpdateEventRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var userId = GetUserIdFromClaims();
        if (userId == null)
            return Unauthorized();

        var ev = await _db.Events
            .Include(e => e.Organizers)
            .Include(e => e.Participants)
            .FirstOrDefaultAsync(e => e.EventId == eventId, ct);

        if (ev == null)
            return NotFound("Event not found.");

        var isOrganizer = ev.Organizers.Any(o => o.UserId == userId && o.Status == MembershipStatus.Active);
        if (!isOrganizer)
            return Forbid("Only organizers can update event details.");

        try
        {
            if (!string.IsNullOrWhiteSpace(req.Title))
            {
                ev.UpdateDetails(userId.Value, req.Title, req.Description ?? (ev.Description ?? ""), req.EventType ?? ev.EventType);
            }

            if (req.StartDate.HasValue)
            {
                if (ev.Status != EventStatus.Draft)
                    return BadRequest("Event date can only be updated before the event starts.");

                ev.Reschedule(userId.Value, DateTime.SpecifyKind(req.StartDate.Value, DateTimeKind.Utc));
            }

            if (!string.IsNullOrWhiteSpace(req.DestinationName))
            {
                ev.SetDestination(req.DestinationName, req.DestinationLatitude ?? 0, req.DestinationLongitude ?? 0);
            }

            await _db.SaveChangesAsync(ct);

            // Broadcast update to all connected clients in this event's group
            var groupName = $"event-{eventId}";
            await _hubContext.Clients.Group(groupName).SendAsync("EventUpdated", new
            {
                ev.EventId,
                ev.Title,
                ev.Description,
                ev.EventType,
                ev.StartDate,
                ev.DestinationName,
                UpdatedBy = userId,
                UpdatedAt = DateTime.UtcNow
            });
            await _hubContext.Clients.Group(groupName).SendAsync("EventDetailsUpdated", new
            {
                ev.EventId,
                Reason = "EventUpdated",
                UpdatedAt = DateTime.UtcNow
            });

            return Ok(new { message = "Event updated successfully", eventId = ev.EventId });
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{eventId}")]
    public async Task<IActionResult> CancelEvent(Guid eventId, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId == null)
            return Unauthorized();

        var ev = await _db.Events
            .Include(e => e.Organizers)
            .FirstOrDefaultAsync(e => e.EventId == eventId, ct);

        if (ev == null)
            return NotFound("Event not found.");

        var isOwner = ev.Organizers.Any(o => o.EventMemberId == ev.OwnerOrganizerId && o.UserId == userId);
        if (!isOwner)
            return Forbid("Only the event owner can cancel the event.");

        try
        {
            if (ev.Status == EventStatus.Draft)
            {
                await DeleteDraftEvent(eventId, ct);
                await _hubContext.Clients.Group($"event-{eventId}").SendAsync("EventDetailsUpdated", new
                {
                    EventId = eventId,
                    Reason = "EventDeleted",
                    UpdatedAt = DateTime.UtcNow
                }, ct);

                return Ok(new { message = "Event deleted successfully" });
            }

            ev.Cancel(userId.Value);
            await _db.SaveChangesAsync(ct);

            // Broadcast cancellation to all connected clients
            var groupName = $"event-{eventId}";
            await _hubContext.Clients.Group(groupName).SendAsync("EventCancelled", new
            {
                ev.EventId,
                ev.Status,
                CancelledAt = DateTime.UtcNow
            });
            await _hubContext.Clients.Group(groupName).SendAsync("EventDetailsUpdated", new
            {
                ev.EventId,
                Reason = "EventCancelled",
                UpdatedAt = DateTime.UtcNow
            });

            return Ok(new { message = "Event cancelled successfully" });
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{eventId}/leave")]
    public async Task<IActionResult> LeaveEvent(Guid eventId, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId == null)
            return Unauthorized();

        var ev = await _db.Events
            .Include(e => e.Organizers)
            .Include(e => e.Participants)
            .FirstOrDefaultAsync(e => e.EventId == eventId, ct);

        if (ev == null)
            return NotFound("Event not found.");

        var member = ev.Organizers.Cast<EventMember>()
            .Concat(ev.Participants)
            .FirstOrDefault(m => m.UserId == userId.Value && m.Status == MembershipStatus.Active);

        if (member is null)
            return NotFound("Membership not found.");

        if (member.EventMemberId == ev.OwnerOrganizerId)
            return BadRequest("The event owner cannot leave. Delete the draft event or transfer ownership first.");

        member.Leave();
        await UnassignDriverFromActivities(eventId, member.EventMemberId, ct);
        await _db.SaveChangesAsync(ct);

        await _hubContext.Clients.Group($"event-{eventId}").SendAsync("EventDetailsUpdated", new
        {
            EventId = eventId,
            Reason = "MemberLeft",
            UpdatedAt = DateTime.UtcNow
        }, ct);

        return Ok(new { message = "You left the event successfully" });
    }

    [HttpGet("{eventId}/members")]
    public async Task<IActionResult> GetEventMembers(Guid eventId, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId == null)
            return Unauthorized();

        var ev = await _db.Events
            .Include(e => e.Organizers)
            .Include(e => e.Participants)
            .FirstOrDefaultAsync(e => e.EventId == eventId, ct);

        if (ev == null)
            return NotFound("Event not found.");

        var isMember = ev.Organizers.Any(o => o.UserId == userId && o.Status == MembershipStatus.Active) ||
                      ev.Participants.Any(p => p.UserId == userId && p.Status == MembershipStatus.Active);

        if (!isMember)
            return Forbid();

        var members = new
        {
            Organizers = ev.Organizers
                .Where(o => o.Status == MembershipStatus.Active)
                .Select(o => new
                {
                    o.EventMemberId,
                    o.UserId,
                    o.JoinedAt,
                    IsOwner = o.EventMemberId == ev.OwnerOrganizerId,
                    o.Status
                })
                .ToList(),
            Participants = ev.Participants
                .Where(p => p.Status == MembershipStatus.Active)
                .Select(p => new
                {
                    p.EventMemberId,
                    p.UserId,
                    p.JoinedAt,
                    p.Mode,
                    p.Status
                })
                .ToList()
        };

        return Ok(members);
    }

    [HttpPost("{eventId}/organizers")]
    public async Task<IActionResult> AddOrganizer(Guid eventId, [FromBody] AddOrganizerRequest req, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId == null)
            return Unauthorized();

        var ev = await _db.Events
            .Include(e => e.Organizers)
            .FirstOrDefaultAsync(e => e.EventId == eventId, ct);

        if (ev == null)
            return NotFound("Event not found.");

        try
        {
            ev.AddOrganizer(userId.Value, req.UserId);
            await _db.SaveChangesAsync(ct);

            // Broadcast new organizer
            var groupName = $"event-{eventId}";
            await _hubContext.Clients.Group(groupName).SendAsync("OrganizerAdded", new
            {
                EventId = eventId,
                NewOrganizerId = req.UserId,
                AddedAt = DateTime.UtcNow
            });
            await _hubContext.Clients.Group(groupName).SendAsync("EventDetailsUpdated", new
            {
                EventId = eventId,
                Reason = "OrganizerAdded",
                UpdatedAt = DateTime.UtcNow
            });

            return Ok(new { message = "Organizer added successfully" });
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{eventId}/participants/{participantId}/mode")]
    public async Task<IActionResult> UpdateParticipantMode(Guid eventId, Guid participantId, [FromBody] UpdateParticipantModeRequest req, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId == null)
            return Unauthorized();

        var ev = await _db.Events
            .Include(e => e.Organizers)
            .Include(e => e.Participants)
            .FirstOrDefaultAsync(e => e.EventId == eventId, ct);

        if (ev == null)
            return NotFound("Event not found.");

        try
        {
            ev.SetParticipantMode(userId.Value, participantId, req.Mode);
            await _db.SaveChangesAsync(ct);

            // Broadcast mode change
            var groupName = $"event-{eventId}";
            await _hubContext.Clients.Group(groupName).SendAsync("ParticipantModeChanged", new
            {
                EventId = eventId,
                ParticipantId = participantId,
                NewMode = req.Mode,
                UpdatedAt = DateTime.UtcNow
            });
            await _hubContext.Clients.Group(groupName).SendAsync("EventDetailsUpdated", new
            {
                EventId = eventId,
                Reason = "ParticipantModeChanged",
                UpdatedAt = DateTime.UtcNow
            });

            return Ok(new { message = "Participant mode updated successfully" });
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    private Guid? GetUserIdFromClaims()
    {
        var uid = User.FindFirstValue("uid");
        return Guid.TryParse(uid, out var userId) ? userId : null;
    }

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

    private async Task DeleteDraftEvent(Guid eventId, CancellationToken ct)
    {
        await using IDbContextTransaction transaction = await _db.Database.BeginTransactionAsync(ct);

        await _db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [Events] SET [OwnerOrganizerId] = NULL WHERE [EventId] = {eventId}", ct);
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM [LocationPoints]
            WHERE [LocationSessionId] IN (
                SELECT [LocationSessionId]
                FROM [LocationSessions]
                WHERE [EventMemberId] IN (
                    SELECT [EventMemberId]
                    FROM [EventMembers]
                    WHERE [EventId] = {eventId}
                )
            )
            """, ct);
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM [LocationSessions]
            WHERE [EventMemberId] IN (
                SELECT [EventMemberId]
                FROM [EventMembers]
                WHERE [EventId] = {eventId}
            )
            """, ct);
        await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [EventLocationGrants] WHERE [EventId] = {eventId}", ct);
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM [ChatMessages]
            WHERE [ChatRoomId] IN (
                SELECT [ChatRoomId]
                FROM [ChatRooms]
                WHERE [EventId] = {eventId}
            )
            """, ct);
        await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [ChatRooms] WHERE [EventId] = {eventId}", ct);
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM [VoiceRecordings]
            WHERE [VoiceSessionId] IN (
                SELECT [VoiceSessionId]
                FROM [VoiceSessions]
                WHERE [VoiceChannelId] IN (
                    SELECT [VoiceChannelId]
                    FROM [VoiceChannels]
                    WHERE [EventId] = {eventId}
                )
            )
            """, ct);
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM [VoiceSessions]
            WHERE [VoiceChannelId] IN (
                SELECT [VoiceChannelId]
                FROM [VoiceChannels]
                WHERE [EventId] = {eventId}
            )
            """, ct);
        await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [VoiceChannels] WHERE [EventId] = {eventId}", ct);
        await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [EventMedia] WHERE [EventId] = {eventId}", ct);
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM [ActivitySteps]
            WHERE [ActivityId] IN (
                SELECT [a].[ActivityId]
                FROM [Activities] AS [a]
                INNER JOIN [EventDays] AS [d] ON [a].[EventDayId] = [d].[EventDayId]
                WHERE [d].[EventId] = {eventId}
            )
            """, ct);
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM [Activities]
            WHERE [EventDayId] IN (
                SELECT [EventDayId]
                FROM [EventDays]
                WHERE [EventId] = {eventId}
            )
            """, ct);
        await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [EventDays] WHERE [EventId] = {eventId}", ct);
        await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [EventMembers] WHERE [EventId] = {eventId}", ct);
        await _db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [Events] WHERE [EventId] = {eventId}", ct);

        await transaction.CommitAsync(ct);
    }

    private static EventDetailsDto ToDetailsDto(Event ev, Guid userId)
    {
        var orderedDays = ev.EventDays
            .OrderBy(d => d.Date)
            .ThenBy(d => d.DayOrder)
            .Select((day, dayIndex) => new EventDayDetailsDto
            {
                EventDayId = day.EventDayId,
                Title = string.IsNullOrWhiteSpace(day.Title) ? $"Day {dayIndex + 1}" : day.Title,
                Date = day.Date,
                Activities = day.Activities
                    .OrderBy(a => a.ActivityOrder)
                    .Select(activity => new ActivityDetailsDto
                    {
                        ActivityId = activity.ActivityId,
                        Title = activity.Title,
                        Type = activity.Type,
                        Status = activity.Status,
                        StartTime = activity.StartTime,
                        EndTime = activity.EndTime,
                        DurationMinutes = activity.DurationMinutes,
                        Order = activity.ActivityOrder,
                        LocationName = activity.LocationName,
                        Latitude = activity.Latitude,
                        Longitude = activity.Longitude,
                        ThumbnailUrl = activity.ThumbnailUrl,
                        DriverParticipantId = activity.DriverParticipantId,
                        DriverDisplayName = activity.DriverDisplayName,
                        Steps = activity.Steps
                            .OrderBy(s => s.StepOrder)
                            .Select(step => new ActivityStepDetailsDto
                            {
                                ActivityStepId = step.ActivityStepId,
                                StepOrder = step.StepOrder,
                                Description = step.Description,
                                IsMandatory = step.IsMandatory
                            })
                            .ToList()
                    })
                    .ToList()
            })
            .ToList();

        return new EventDetailsDto
        {
            EventId = ev.EventId,
            Title = ev.Title,
            Description = ev.Description,
            EventType = ev.EventType,
            StartDate = ev.StartDate,
            EndDate = ev.EndDate,
            Status = ev.Status,
            DestinationName = ev.DestinationName,
            DestinationLatitude = ev.DestinationLatitude,
            DestinationLongitude = ev.DestinationLongitude,
            ThumbnailUrl = ev.ThumbnailUrl,
            CreatedAt = ev.CreatedAt,
            JoinCode = ev.JoinCode,
            IsJoinEnabled = ev.IsJoinEnabled,
            EventMemberId = ev.Organizers.Cast<EventMember>()
                .Concat(ev.Participants)
                .Where(m => m.UserId == userId && m.Status == MembershipStatus.Active)
                .Select(m => (Guid?)m.EventMemberId)
                .FirstOrDefault(),
            Role = ev.Organizers.Any(o => o.UserId == userId && o.EventMemberId == ev.OwnerOrganizerId && o.Status == MembershipStatus.Active) ? "Owner" :
                   ev.Organizers.Any(o => o.UserId == userId && o.Status == MembershipStatus.Active) ? "Organizer" :
                   ev.Participants.Any(p => p.UserId == userId && p.Status == MembershipStatus.Active) ? "Participant" : "Unknown",
            ParticipantMode = ev.Participants
                .Where(p => p.UserId == userId && p.Status == MembershipStatus.Active)
                .Select(p => (ParticipantMode?)p.Mode)
                .FirstOrDefault(),
            OrganizersCount = ev.Organizers.Count(o => o.Status == MembershipStatus.Active),
            ParticipantsCount = ev.Participants.Count(p => p.Status == MembershipStatus.Active),
            Days = orderedDays
        };
    }
}

public sealed class UpdateEventRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? EventType { get; set; }
    public DateTime? StartDate { get; set; }
    public string? DestinationName { get; set; }
    public double? DestinationLatitude { get; set; }
    public double? DestinationLongitude { get; set; }
}

public sealed class AddOrganizerRequest
{
    public Guid UserId { get; set; }
}

public sealed class UpdateParticipantModeRequest
{
    public ParticipantMode Mode { get; set; }
}

// Response DTOs
public sealed class EventListItemDto
{
    public Guid EventId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public EventStatus Status { get; set; }
    public string DestinationName { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class UserEventDto
{
    public Guid EventId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public EventStatus Status { get; set; }
    public string DestinationName { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? EventMemberId { get; set; }
    public string Role { get; set; } = "Unknown"; // "Owner", "Organizer", "Participant"
    public ParticipantMode? ParticipantMode { get; set; }
}

public sealed class EventDetailsDto
{
    public Guid EventId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public EventStatus Status { get; set; }
    public string DestinationName { get; set; } = string.Empty;
    public double DestinationLatitude { get; set; }
    public double DestinationLongitude { get; set; }
    public string? ThumbnailUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public string JoinCode { get; set; } = string.Empty;
    public bool IsJoinEnabled { get; set; }
    public Guid? EventMemberId { get; set; }
    public string Role { get; set; } = "Unknown";
    public ParticipantMode? ParticipantMode { get; set; }
    public int OrganizersCount { get; set; }
    public int ParticipantsCount { get; set; }
    public List<EventDayDetailsDto> Days { get; set; } = new();
}

public sealed class EventDayDetailsDto
{
    public Guid EventDayId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public List<ActivityDetailsDto> Activities { get; set; } = new();
}

public sealed class ActivityDetailsDto
{
    public Guid ActivityId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public ActivityStatus Status { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public int DurationMinutes { get; set; }
    public int Order { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? ThumbnailUrl { get; set; }
    public Guid? DriverParticipantId { get; set; }
    public string? DriverDisplayName { get; set; }
    public List<ActivityStepDetailsDto> Steps { get; set; } = new();
}

public sealed class ActivityStepDetailsDto
{
    public Guid ActivityStepId { get; set; }
    public int StepOrder { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsMandatory { get; set; }
}
