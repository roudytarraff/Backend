using System.Security.Claims;
using System.Text.Json;
using Backend.Services.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TripPlanner.Api.Data;
using TripPlanner.Api.Domain;
using TripPlanner.Api.Domain.Events;
using TripPlanner.Api.Domain.Location;
using TripPlanner.Api.Domain.Schedule;

namespace Backend.Controllers.EventOrganizer;

[ApiController]
[Route("api/events/{eventId:guid}/organizer-workspace")]
[Authorize]
public sealed class OrganizerWorkspaceController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IBlobStorageService _blobStorage;
    private readonly IHubContext<Hubs.EventHub> _hubContext;

    public OrganizerWorkspaceController(
        AppDbContext db,
        IBlobStorageService blobStorage,
        IHubContext<Hubs.EventHub> hubContext)
    {
        _db = db;
        _blobStorage = blobStorage;
        _hubContext = hubContext;
    }

    [HttpGet]
    public async Task<IActionResult> Details(Guid eventId, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();

        var ev = await LoadFullEvent(eventId, ct);
        if (ev is null) return NotFound("Event not found.");
        if (!IsActiveOrganizer(ev, userId.Value)) return Forbid();

        return Ok(await ToWorkspaceDto(ev, ct));
    }

    [HttpPost("activities/{activityId:guid}/start")]
    public async Task<IActionResult> StartActivity(Guid eventId, Guid activityId, [FromBody] StartOrganizerActivityRequest? req, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();

        var result = await LoadOrganizerActivity(eventId, activityId, userId.Value, ct);
        if (result.Error is not null) return result.Error;
        if (result.Event!.Status != EventStatus.Active)
            return BadRequest("Start the event before starting activities.");

        var selectedDay = result.Event!.EventDays.FirstOrDefault(d => d.Activities.Any(a => a.ActivityId == activityId));
        if (selectedDay is null) return NotFound("Event day not found.");

        var selectedActivity = result.Activity!;
        var startedAt = !string.IsNullOrWhiteSpace(req?.StartTime)
            ? CombineDayAndTime(selectedDay.Date, req.StartTime)
            : DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);

        var previousActivities = selectedDay.Activities
            .Where(a => a.ActivityOrder < selectedActivity.ActivityOrder)
            .Where(a => a.Status != ActivityStatus.Ended)
            .ToList();

        foreach (var previous in previousActivities)
        {
            previous.EndFromOrganizerProgress(startedAt);
        }

        selectedActivity.Start(startedAt);
        await _db.SaveChangesAsync(ct);
        await BroadcastActivityChanged(eventId, selectedActivity, "ActivityStarted");

        return Ok(new { selectedActivity.ActivityId, selectedActivity.Status, selectedActivity.StartTime, selectedActivity.EndTime });
    }

    [HttpPost("days")]
    public async Task<IActionResult> AddDay(Guid eventId, [FromBody] AddOrganizerDayRequest req, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();

        var ev = await LoadFullEvent(eventId, ct);
        if (ev is null) return NotFound("Event not found.");
        if (!IsActiveOrganizer(ev, userId.Value)) return Forbid();
        if (ev.Status is EventStatus.Cancelled or EventStatus.Completed)
            return BadRequest("Days cannot be added to a closed event.");

        var day = ev.AddEventDay(userId.Value, DateTime.SpecifyKind(req.Date.Date, DateTimeKind.Utc), req.Title);
        await _db.SaveChangesAsync(ct);
        await BroadcastDetailsChanged(eventId, "DayAdded");

        return Ok(new { day.EventDayId });
    }

    [HttpDelete("days/{eventDayId:guid}")]
    public async Task<IActionResult> DeleteDay(Guid eventId, Guid eventDayId, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();

        var ev = await LoadFullEvent(eventId, ct);
        if (ev is null) return NotFound("Event not found.");
        if (!IsActiveOrganizer(ev, userId.Value)) return Forbid();
        if (ev.Status is EventStatus.Cancelled or EventStatus.Completed)
            return BadRequest("Days cannot be deleted from a closed event.");
        if (ev.EventDays.Count <= 1)
            return BadRequest("The event needs at least one day.");

        var day = ev.EventDays.FirstOrDefault(d => d.EventDayId == eventDayId);
        if (day is null) return NotFound("Event day not found.");

        ev.RemoveEventDay(userId.Value, eventDayId);

        foreach (var orderedDay in ev.EventDays.OrderBy(d => d.Date).ThenBy(d => d.DayOrder).Select((dayItem, index) => new { dayItem, index }))
        {
            orderedDay.dayItem.SetOrder(orderedDay.index);
        }

        await _db.SaveChangesAsync(ct);
        await BroadcastDetailsChanged(eventId, "DayDeleted");

        return Ok();
    }

    [HttpPost("days/{eventDayId:guid}/activities")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> AddActivity(Guid eventId, Guid eventDayId, [FromForm] UpdateOrganizerActivityRequest req, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();

        var ev = await LoadFullEvent(eventId, ct);
        if (ev is null) return NotFound("Event not found.");
        if (!IsActiveOrganizer(ev, userId.Value)) return Forbid();
        if (ev.Status is EventStatus.Cancelled or EventStatus.Completed)
            return BadRequest("Activities cannot be added to a closed event.");

        var day = ev.EventDays.FirstOrDefault(d => d.EventDayId == eventDayId);
        if (day is null) return NotFound("Event day not found.");

        var activity = new Activity(eventDayId, req.Title, req.Type, req.DurationMinutes, day.Activities.Count);
        activity.UpdateLocation(req.LocationName, req.Latitude, req.Longitude);

        if (!string.IsNullOrWhiteSpace(req.StartTime))
        {
            activity.UpdateStartTime(CombineDayAndTime(day.Date, req.StartTime));
        }

        if (req.CoverImage is not null)
        {
            var url = await _blobStorage.UploadImageAsync(req.CoverImage, "activities", ct);
            activity.UpdateThumbnail(url);
        }

        var requestedSteps = ParseSteps(req.StepsJson);
        foreach (var step in requestedSteps
            .Where(s => !string.IsNullOrWhiteSpace(s.Description))
            .Select((step, index) => new ActivityStep(activity.ActivityId, index, step.Description, step.IsMandatory)))
        {
            activity.AddStep(step);
        }

        day.AddActivity(activity);
        await _db.SaveChangesAsync(ct);
        await BroadcastActivityChanged(eventId, activity, "ActivityAdded");

        return Ok(new { activity.ActivityId });
    }

    [HttpPost("activities/{activityId:guid}/end")]
    public async Task<IActionResult> EndActivity(Guid eventId, Guid activityId, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();

        var result = await LoadOrganizerActivity(eventId, activityId, userId.Value, ct);
        if (result.Error is not null) return result.Error;
        if (result.Event!.Status != EventStatus.Active)
            return BadRequest("Start the event before ending activities.");

        result.Activity!.End();
        await _db.SaveChangesAsync(ct);
        await BroadcastActivityChanged(eventId, result.Activity, "ActivityEnded");

        return Ok(new { result.Activity.ActivityId, result.Activity.Status, result.Activity.StartTime, result.Activity.EndTime });
    }

    [HttpPost("activities/{activityId:guid}/reset")]
    public async Task<IActionResult> ResetActivity(Guid eventId, Guid activityId, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();

        var result = await LoadOrganizerActivity(eventId, activityId, userId.Value, ct);
        if (result.Error is not null) return result.Error;
        if (result.Event!.Status != EventStatus.Active)
            return BadRequest("Start the event before resetting activities.");

        result.Activity!.ResetToNotStarted();
        await _db.SaveChangesAsync(ct);
        await BroadcastActivityChanged(eventId, result.Activity, "ActivityReset");

        return Ok(new { result.Activity.ActivityId, result.Activity.Status, result.Activity.StartTime, result.Activity.EndTime });
    }

    [HttpPut("activities/{activityId:guid}")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UpdateActivity(Guid eventId, Guid activityId, [FromForm] UpdateOrganizerActivityRequest req, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();

        var result = await LoadOrganizerActivity(eventId, activityId, userId.Value, ct);
        if (result.Error is not null) return result.Error;

        var activity = result.Activity!;
        activity.UpdateDetails(
            req.Title,
            req.Type,
            req.DurationMinutes,
            req.LocationName,
            req.Latitude,
            req.Longitude);

        if (!string.IsNullOrWhiteSpace(req.StartTime))
        {
            var day = result.Event!.EventDays.FirstOrDefault(d => d.Activities.Any(a => a.ActivityId == activityId));
            if (day is null) return NotFound("Event day not found.");
            activity.UpdateStartTime(CombineDayAndTime(day.Date, req.StartTime));
        }

        if (req.CoverImage is not null)
        {
            var url = await _blobStorage.UploadImageAsync(req.CoverImage, "activities", ct);
            activity.UpdateThumbnail(url);
        }

        var requestedSteps = ParseSteps(req.StepsJson);

        var steps = requestedSteps
            .Where(s => !string.IsNullOrWhiteSpace(s.Description))
            .Select((step, index) => new ActivityStep(activity.ActivityId, index, step.Description, step.IsMandatory))
            .ToList();
        activity.ReplaceSteps(steps);

        await _db.SaveChangesAsync(ct);
        await BroadcastActivityChanged(eventId, activity, "ActivityUpdated");

        return Ok(new { activity.ActivityId });
    }

    [HttpDelete("activities/{activityId:guid}")]
    public async Task<IActionResult> DeleteActivity(Guid eventId, Guid activityId, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();

        var ev = await LoadFullEvent(eventId, ct);
        if (ev is null) return NotFound("Event not found.");
        if (!IsActiveOrganizer(ev, userId.Value)) return Forbid();

        var day = ev.EventDays.FirstOrDefault(d => d.Activities.Any(a => a.ActivityId == activityId));
        if (day is null) return NotFound("Activity not found.");
        var removedActivity = day.Activities.First(a => a.ActivityId == activityId);

        day.RemoveActivity(activityId);

        foreach (var activity in day.Activities.OrderBy(a => a.ActivityOrder).Select((activity, index) => new { activity, index }))
        {
            activity.activity.SetOrder(activity.index);
        }

        await _db.SaveChangesAsync(ct);
        await BroadcastActivityChanged(eventId, removedActivity, "ActivityDeleted");

        return Ok();
    }

    [HttpPut("days/{eventDayId:guid}/activity-order")]
    public async Task<IActionResult> ReorderActivities(Guid eventId, Guid eventDayId, [FromBody] ReorderActivitiesRequest req, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();

        var ev = await LoadFullEvent(eventId, ct);
        if (ev is null) return NotFound("Event not found.");
        if (!IsActiveOrganizer(ev, userId.Value)) return Forbid();

        var day = ev.EventDays.FirstOrDefault(d => d.EventDayId == eventDayId);
        if (day is null) return NotFound("Event day not found.");

        var requestedIds = req.ActivityIds.Distinct().ToList();
        if (requestedIds.Count != day.Activities.Count || day.Activities.Any(a => !requestedIds.Contains(a.ActivityId)))
        {
            return BadRequest("Activity order must include every activity in the selected day exactly once.");
        }

        for (var index = 0; index < requestedIds.Count; index++)
        {
            var activity = day.Activities.First(a => a.ActivityId == requestedIds[index]);
            activity.SetOrder(index);
        }

        await _db.SaveChangesAsync(ct);
        await BroadcastDetailsChanged(eventId, "ActivityOrderUpdated");

        return Ok();
    }

    [HttpPost("locations/me")]
    public async Task<IActionResult> UpdateMyLocation(Guid eventId, [FromBody] UpdateMemberLocationRequest req, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();

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

        await _hubContext.Clients.Group($"event-{eventId}").SendAsync("MemberLocationUpdated", new
        {
            EventId = eventId,
            member.EventMemberId,
            member.UserId,
            req.Latitude,
            req.Longitude,
            req.Accuracy,
            point.RecordedAt
        });

        return Ok();
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

    private async Task<Event?> LoadFullEvent(Guid eventId, CancellationToken ct)
        => await _db.Events
            .AsSplitQuery()
            .Include(e => e.Organizers)
                .ThenInclude(m => m.LocationSession)
            .Include(e => e.Participants)
                .ThenInclude(m => m.LocationSession)
            .Include(e => e.EventDays)
                .ThenInclude(d => d.Activities)
                    .ThenInclude(a => a.Steps)
            .FirstOrDefaultAsync(e => e.EventId == eventId, ct);

    private async Task<ActivityLoadResult> LoadOrganizerActivity(Guid eventId, Guid activityId, Guid userId, CancellationToken ct)
    {
        var ev = await LoadFullEvent(eventId, ct);
        if (ev is null) return new ActivityLoadResult(null, null, NotFound("Event not found."));
        if (!IsActiveOrganizer(ev, userId)) return new ActivityLoadResult(null, null, Forbid());

        var activity = ev.EventDays
            .SelectMany(d => d.Activities)
            .FirstOrDefault(a => a.ActivityId == activityId);

        return activity is null
            ? new ActivityLoadResult(ev, null, NotFound("Activity not found."))
            : new ActivityLoadResult(ev, activity, null);
    }

    private async Task<OrganizerWorkspaceDto> ToWorkspaceDto(Event ev, CancellationToken ct)
    {
        var memberUserIds = ev.Organizers.Cast<EventMember>()
            .Concat(ev.Participants)
            .Where(m => m.Status == MembershipStatus.Active)
            .Select(m => m.UserId)
            .Distinct()
            .ToList();

        var users = await _db.Users
            .AsNoTracking()
            .Where(u => memberUserIds.Contains(u.UserId))
            .ToDictionaryAsync(u => u.UserId, ct);

        var memberSessions = ev.Organizers.Cast<EventMember>()
            .Concat(ev.Participants)
            .Where(m => m.Status == MembershipStatus.Active && m.LocationSession != null)
            .Select(m => m.LocationSession!)
            .ToList();

        var sessionIds = memberSessions
            .Select(s => s.LocationSessionId)
            .Distinct()
            .ToList();

        var latestPointRows = await _db.LocationPoints
            .AsNoTracking()
            .Where(p => sessionIds.Contains(p.LocationSessionId))
            .OrderByDescending(p => p.RecordedAt)
            .Select(p => new LatestLocationPointDto
            {
                LocationSessionId = p.LocationSessionId,
                Latitude = p.Latitude,
                Longitude = p.Longitude,
                Accuracy = p.Accuracy,
                RecordedAt = p.RecordedAt
            })
            .ToListAsync(ct);

        var latestPoints = latestPointRows
            .GroupBy(p => p.LocationSessionId)
            .ToDictionary(g => g.Key, g => g.First());

        return new OrganizerWorkspaceDto
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
            Days = ev.EventDays
                .OrderBy(d => d.Date)
                .ThenBy(d => d.DayOrder)
                .Select((day, dayIndex) => new OrganizerDayDto
                {
                    EventDayId = day.EventDayId,
                    Title = string.IsNullOrWhiteSpace(day.Title) ? $"Day {dayIndex + 1}" : day.Title,
                    Date = day.Date,
                    Activities = day.Activities
                        .OrderBy(a => a.ActivityOrder)
                        .Select(a => new OrganizerActivityDto
                        {
                            ActivityId = a.ActivityId,
                            Title = a.Title,
                            Type = a.Type,
                            Status = a.Status,
                            StartTime = a.StartTime,
                            EndTime = a.EndTime,
                            DurationMinutes = a.DurationMinutes,
                            Order = a.ActivityOrder,
                            LocationName = a.LocationName,
                            Latitude = a.Latitude,
                            Longitude = a.Longitude,
                            ThumbnailUrl = a.ThumbnailUrl,
                            Steps = a.Steps
                                .OrderBy(s => s.StepOrder)
                                .Select(s => new OrganizerActivityStepDto
                                {
                                    ActivityStepId = s.ActivityStepId,
                                    StepOrder = s.StepOrder,
                                    Description = s.Description,
                                    IsMandatory = s.IsMandatory
                                })
                                .ToList()
                        })
                        .ToList()
                })
                .ToList(),
            Members = ev.Organizers.Cast<EventMember>()
                .Concat(ev.Participants)
                .Where(m => m.Status == MembershipStatus.Active)
                .Select(m => ToMemberDto(m, ev, users, latestPoints))
                .ToList()
        };
    }

    private static OrganizerMemberDto ToMemberDto(
        EventMember member,
        Event ev,
        Dictionary<Guid, TripPlanner.Api.Domain.Users.User> users,
        Dictionary<Guid, LatestLocationPointDto> latestPoints)
    {
        users.TryGetValue(member.UserId, out var user);
        LatestLocationPointDto? lastPoint = null;
        if (member.LocationSession != null)
        {
            latestPoints.TryGetValue(member.LocationSession.LocationSessionId, out lastPoint);
        }

        return new OrganizerMemberDto
        {
            EventMemberId = member.EventMemberId,
            UserId = member.UserId,
            FullName = user is null ? "Unknown User" : $"{user.FirstName} {user.LastName}",
            Email = user?.Email ?? "",
            ProfilePictureUrl = user?.ProfilePictureUrl,
            Role = member.EventMemberId == ev.OwnerOrganizerId ? "Owner" : member is Organizer ? "Organizer" : "Participant",
            ParticipantMode = member is Participant p ? p.Mode : null,
            Latitude = lastPoint?.Latitude,
            Longitude = lastPoint?.Longitude,
            Accuracy = lastPoint?.Accuracy,
            LocationRecordedAt = lastPoint?.RecordedAt
        };
    }

    private static bool IsActiveOrganizer(Event ev, Guid userId)
        => ev.Organizers.Any(o => o.UserId == userId && o.Status == MembershipStatus.Active);

    private Task BroadcastActivityChanged(Guid eventId, Activity activity, string reason)
        => _hubContext.Clients.Group($"event-{eventId}").SendAsync("EventDetailsUpdated", new
        {
            EventId = eventId,
            ActivityId = activity.ActivityId,
            ActivityTitle = activity.Title,
            ActivityStatus = activity.Status,
            ActivityStartTime = activity.StartTime,
            ActivityEndTime = activity.EndTime,
            ActivityWindow = FormatActivityWindow(activity.StartTime, activity.EndTime),
            SentAt = ActivityNotificationTime(activity, reason),
            Reason = reason,
            UpdatedAt = DateTime.UtcNow
        });

    private Task BroadcastDetailsChanged(Guid eventId, string reason)
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

    private static string? FormatActivityWindow(DateTime? start, DateTime? end)
    {
        if (start is null && end is null) return null;
        var left = start?.ToString("HH:mm") ?? "?";
        var right = end?.ToString("HH:mm") ?? "?";
        return $"{left} - {right}";
    }

    private static DateTime ActivityNotificationTime(Activity activity, string reason)
    {
        if (reason.Contains("Ended", StringComparison.OrdinalIgnoreCase))
            return activity.EndTime ?? activity.StartTime ?? DateTime.UtcNow;

        if (reason.Contains("Started", StringComparison.OrdinalIgnoreCase))
            return activity.StartTime ?? activity.EndTime ?? DateTime.UtcNow;

        return activity.StartTime ?? activity.EndTime ?? DateTime.UtcNow;
    }

    private static DateTime CombineDayAndTime(DateTime dayDate, string time)
    {
        if (!TimeSpan.TryParse(time, out var parsedTime))
        {
            throw new ArgumentException("Invalid start time.");
        }

        return DateTime.SpecifyKind(dayDate.Date.Add(parsedTime), DateTimeKind.Unspecified);
    }

    private static List<OrganizerActivityStepInput> ParseSteps(string? stepsJson)
        => string.IsNullOrWhiteSpace(stepsJson)
            ? new List<OrganizerActivityStepInput>()
            : JsonSerializer.Deserialize<List<OrganizerActivityStepInput>>(stepsJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new List<OrganizerActivityStepInput>();

    private sealed record ActivityLoadResult(Event? Event, Activity? Activity, IActionResult? Error);
}

public sealed class UpdateOrganizerActivityRequest
{
    public string Title { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public string? StartTime { get; set; }
    public string LocationName { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? StepsJson { get; set; }
    public IFormFile? CoverImage { get; set; }
}

public sealed class StartOrganizerActivityRequest
{
    public string? StartTime { get; set; }
}

public sealed class AddOrganizerDayRequest
{
    public string Title { get; set; } = string.Empty;
    public DateTime Date { get; set; }
}

public sealed class OrganizerActivityStepInput
{
    public string Description { get; set; } = string.Empty;
    public bool IsMandatory { get; set; } = true;
}

public sealed class ReorderActivitiesRequest
{
    public List<Guid> ActivityIds { get; set; } = new();
}

public sealed class UpdateMemberLocationRequest
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Accuracy { get; set; }
}

public sealed class OrganizerWorkspaceDto
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
    public List<OrganizerDayDto> Days { get; set; } = new();
    public List<OrganizerMemberDto> Members { get; set; } = new();
}

public sealed class LatestLocationPointDto
{
    public Guid LocationSessionId { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Accuracy { get; set; }
    public DateTime RecordedAt { get; set; }
}

public sealed class OrganizerDayDto
{
    public Guid EventDayId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public List<OrganizerActivityDto> Activities { get; set; } = new();
}

public sealed class OrganizerActivityDto
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
    public List<OrganizerActivityStepDto> Steps { get; set; } = new();
}

public sealed class OrganizerActivityStepDto
{
    public Guid ActivityStepId { get; set; }
    public int StepOrder { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsMandatory { get; set; }
}

public sealed class OrganizerMemberDto
{
    public Guid EventMemberId { get; set; }
    public Guid UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? ProfilePictureUrl { get; set; }
    public string Role { get; set; } = string.Empty;
    public ParticipantMode? ParticipantMode { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? Accuracy { get; set; }
    public DateTime? LocationRecordedAt { get; set; }
}
