using System.ComponentModel.DataAnnotations;
using TripPlanner.Api.Common;

namespace TripPlanner.Api.Domain.Location;

public sealed class LocationSession
{
    private LocationSession() { } // EF

    public const int MaxPoints = 50;

    [Key]
    public Guid LocationSessionId { get; private set; }

    public Guid EventMemberId { get; private set; }

    public DateTime StartedAt { get; private set; }
    public DateTime? EndedAt { get; private set; }
    public bool IsActive { get; private set; }

    public List<LocationPoint> Points { get; private set; } = new();

    public LocationSession(Guid eventMemberId)
    {
        LocationSessionId = Guid.NewGuid();
        EventMemberId = Guard.NotEmpty(eventMemberId, nameof(eventMemberId));
        StartedAt = DateTime.UtcNow;
        IsActive = true;
    }

    public void Start()
    {
        if (IsActive) return;
        IsActive = true;
        StartedAt = DateTime.UtcNow;
        EndedAt = null;
    }

    public void Stop()
    {
        if (!IsActive) return;
        IsActive = false;
        EndedAt ??= DateTime.UtcNow;
    }

    public void AddLocationPoint(LocationPoint point)
    {
        Guard.Ensure(IsActive, "Location session is not active.");
        Guard.Ensure(point is not null, "Point is required.");

        // ✅ cap storage at 500 (FIFO)
        Points.Add(point);
    }
}
