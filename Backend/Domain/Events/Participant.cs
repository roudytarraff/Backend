using TripPlanner.Api.Common;

namespace TripPlanner.Api.Domain.Events;

public sealed class Participant : EventMember
{
    private Participant() { } // EF

    public ParticipantMode Mode { get; private set; }

    public Participant(Guid eventId, Guid userId, ParticipantMode mode)
    {
        EventMemberId = Guid.NewGuid();
        EventId = Guard.NotEmpty(eventId, nameof(eventId));
        UserId = Guard.NotEmpty(userId, nameof(userId));
        JoinedAt = DateTime.UtcNow;
        Status = MembershipStatus.Active;
        Mode = mode;
    }

    public void SetMode(ParticipantMode mode) => Mode = mode;
}
