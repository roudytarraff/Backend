using System.ComponentModel.DataAnnotations;
using TripPlanner.Api.Common;

namespace TripPlanner.Api.Domain.Location;

public sealed class LocationPoint
{
    private LocationPoint() { } // EF

    [Key]
    public Guid LocationPointId { get; private set; }

    public Guid LocationSessionId { get; private set; }

    public double Latitude { get; private set; }
    public double Longitude { get; private set; }

    public double Accuracy { get; private set; }
    public DateTime RecordedAt { get; private set; }

    public LocationPoint(Guid locationSessionId, double lat, double lng, double accuracy)
    {
        LocationPointId = Guid.NewGuid();
        LocationSessionId = Guard.NotEmpty(locationSessionId, nameof(locationSessionId));
        Latitude = lat;
        Longitude = lng;
        Accuracy = Guard.NonNegative(accuracy, nameof(Accuracy));
        RecordedAt = DateTime.UtcNow;
    }
}
