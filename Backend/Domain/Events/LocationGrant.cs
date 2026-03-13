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

    public LocationGrant(Guid byMemberId, Guid toMemberId)
    {
        LocationGrantId = Guid.NewGuid();
        GrantedByMemberId = Guard.NotEmpty(byMemberId, nameof(GrantedByMemberId));
        GrantedToMemberId = Guard.NotEmpty(toMemberId, nameof(GrantedToMemberId));
        Guard.Ensure(GrantedByMemberId != GrantedToMemberId, "Cannot grant to self.");
        CreatedAt = DateTime.UtcNow;
        IsActive = true;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}
