using System.ComponentModel.DataAnnotations;
using TripPlanner.Api.Common;
using TripPlanner.Api.Domain;

namespace TripPlanner.Api.Domain.Schedule;

public sealed class Activity
{
    private Activity() { } // EF

    [Key]
    public Guid ActivityId { get; private set; }

    public Guid EventDayId { get; private set; }

    [MaxLength(120)]
    public string Title { get; private set; } = null!;

    [MaxLength(60)]
    public string Type { get; private set; } = null!;

    // ✅ removed Description

    public ActivityStatus Status { get; private set; }

    // ✅ timestamps filled by lifecycle
    public DateTime? StartTime { get; private set; }
    public DateTime? EndTime { get; private set; }

    // ✅ primary time field
    public int DurationMinutes { get; private set; }

    [MaxLength(2048)]
    public string? ThumbnailUrl { get; private set; }

    [MaxLength(120)]
    public string LocationName { get; private set; } = "";

    public double Latitude { get; private set; }
    public double Longitude { get; private set; }

    public int ActivityOrder { get; private set; }

    public List<ActivityStep> Steps { get; private set; } = new();

    public Activity(Guid eventDayId, string title, string type, int durationMinutes, int order)
    {
        ActivityId = Guid.NewGuid();
        EventDayId = Guard.NotEmpty(eventDayId, nameof(eventDayId));

        Title = Guard.Required(title, nameof(Title), 120);
        Type = Guard.Required(type, nameof(Type), 60);

        DurationMinutes = Guard.EnsureDurationMinutes(durationMinutes);
        ActivityOrder = Guard.NonNegative(order, nameof(ActivityOrder));

        Status = ActivityStatus.NotStarted;
        StartTime = null;
        EndTime = null;
    }

    public void Rename(string title) => Title = Guard.Required(title, nameof(Title), 120);

    public void UpdateDetails(string title, string type, int durationMinutes, string locationName, double latitude, double longitude)
    {
        Title = Guard.Required(title, nameof(Title), 120);
        Type = Guard.Required(type, nameof(Type), 60);
        UpdateDuration(durationMinutes);
        UpdateLocation(locationName, latitude, longitude);
    }

    // ✅ update duration (this is your "UpdateTime" new meaning)
    public void UpdateDuration(int durationMinutes)
    {
        DurationMinutes = Guard.EnsureDurationMinutes(durationMinutes);

        if (StartTime is not null)
        {
            EndTime = StartTime.Value.AddMinutes(DurationMinutes);
        }
    }

    public void UpdateThumbnail(string? url)
        => ThumbnailUrl = Guard.UrlOrNull(url, nameof(ThumbnailUrl), 2048);

    public void UpdateLocation(string name, double lat, double lng)
    {
        LocationName = Guard.Required(name, nameof(LocationName), 120);
        Latitude = lat;
        Longitude = lng;
    }

    public void SetOrder(int order)
        => ActivityOrder = Guard.NonNegative(order, nameof(ActivityOrder));

    public void UpdateStartTime(DateTime? startTime)
    {
        StartTime = startTime;
        EndTime = startTime?.AddMinutes(DurationMinutes);
    }

    // ✅ status-driven timestamps
    public void Start(DateTime startedAt)
    {
        Guard.Ensure(Status == ActivityStatus.NotStarted, "Activity can only be started from NotStarted.");
        Status = ActivityStatus.Ongoing;
        StartTime = startedAt;
        EndTime = startedAt.AddMinutes(DurationMinutes);
    }

    public void End()
    {
        Guard.Ensure(Status == ActivityStatus.Ongoing, "Activity can only be ended from Ongoing.");
        Guard.Ensure(StartTime is not null, "StartTime missing.");

        Status = ActivityStatus.Ended;

        // your rule: EndTime gets filled based on status
        // choose strict duration or actual end time:
        EndTime = StartTime.Value.AddMinutes(DurationMinutes);
    }

    public void EndFromOrganizerProgress(DateTime endedAtUtc)
    {
        if (Status == ActivityStatus.Ended) return;

        StartTime ??= endedAtUtc.AddMinutes(-DurationMinutes);
        EndTime = StartTime.Value.AddMinutes(DurationMinutes);
        Status = ActivityStatus.Ended;
    }

    public void ResetToNotStarted()
    {
        Status = ActivityStatus.NotStarted;
        StartTime = null;
        EndTime = null;
    }

    public void AddStep(ActivityStep step)
    {
        Guard.Ensure(step is not null, "Step is required.");
        Steps.Add(step);
    }

    public void ReplaceSteps(IEnumerable<ActivityStep> steps)
    {
        Steps.Clear();
        Steps.AddRange(steps);
    }

    public void RemoveStep(Guid stepId)
    {
        var s = Steps.FirstOrDefault(x => x.ActivityStepId == stepId);
        Guard.Ensure(s is not null, "Step not found.");
        Steps.Remove(s);
    }
}
