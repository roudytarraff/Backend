using System.ComponentModel.DataAnnotations;
using TripPlanner.Api.Domain.Location;

namespace TripPlanner.Api.Domain.Events;

public abstract class EventMember
{
    protected EventMember() { } // EF

    [Key]
    public Guid EventMemberId { get; protected set; }

    public Guid EventId { get; protected set; }
    public Guid UserId { get; protected set; }

    public DateTime JoinedAt { get; protected set; }
    public MembershipStatus Status { get; protected set; }=MembershipStatus.Active;

    public LocationSession? LocationSession { get; protected set; }

    public void Activate() => Status = MembershipStatus.Active;

    // ✅ no reason
    public void Suspend() => Status = MembershipStatus.Suspended;

    public void Leave() => Status = MembershipStatus.Left;

    // ✅ no reason
    public void Remove() => Status = MembershipStatus.Removed;
}
