using System.ComponentModel.DataAnnotations;
using TripPlanner.Api.Common;

namespace TripPlanner.Api.Domain.Events;

public sealed class LocationGrant
{
    private LocationGrant() { } // EF

    [Key]
    public Guid LocationGrantId { get; private set; }

    public Guid GrantedByMemberId { get; private set; }
    public Guid GrantedToMemberId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public bool IsActive { get; private set; }
    public LocationGrantStatus Status { get; private set; }

    public LocationGrant(Guid byMemberId, Guid toMemberId, LocationGrantStatus status = LocationGrantStatus.Active)
    {
        LocationGrantId = Guid.NewGuid();
        GrantedByMemberId = Guard.NotEmpty(byMemberId, nameof(GrantedByMemberId));
        GrantedToMemberId = Guard.NotEmpty(toMemberId, nameof(GrantedToMemberId));
        Guard.Ensure(GrantedByMemberId != GrantedToMemberId, "Cannot grant to self.");
        CreatedAt = DateTime.UtcNow;
        Status = status;
        IsActive = status == LocationGrantStatus.Active;
    }

    public void MarkPending()
    {
        Status = LocationGrantStatus.Pending;
        IsActive = false;
    }

    public void Activate()
    {
        Status = LocationGrantStatus.Active;
        IsActive = true;
    }

    public void Deactivate()
    {
        Status = LocationGrantStatus.Pending;
        IsActive = false;
    }
}
