using System.ComponentModel.DataAnnotations;
using TripPlanner.Api.Common;

namespace TripPlanner.Api.Domain.Events;

public sealed class Organizer : EventMember
{
    private Organizer() { } // EF

    [MaxLength(60)]
    public string? DisplayTitle { get; private set; }

    public Organizer(Guid eventId, Guid userId)
    {
        EventMemberId = Guid.NewGuid();
        EventId = Guard.NotEmpty(eventId, nameof(eventId));
        UserId = Guard.NotEmpty(userId, nameof(userId));
        JoinedAt = DateTime.UtcNow;
        Status = MembershipStatus.Active;
    }

    public void RenameTitle(string? title)
        => DisplayTitle = string.IsNullOrWhiteSpace(title) ? null : Guard.Required(title, nameof(DisplayTitle), 60);
}
