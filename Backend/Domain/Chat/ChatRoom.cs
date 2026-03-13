using System.ComponentModel.DataAnnotations;
using TripPlanner.Api.Common;

namespace TripPlanner.Api.Domain.Chat;

public sealed class ChatRoom
{
    private ChatRoom() { } // EF

    [Key]
    public Guid ChatRoomId { get; private set; }

    public Guid EventId { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public List<ChatMessage> Messages { get; private set; } = new();

    public ChatRoom(Guid eventId)
    {
        ChatRoomId = Guid.NewGuid();
        EventId = Guard.NotEmpty(eventId, nameof(eventId));
        CreatedAt = DateTime.UtcNow;
    }

    public void AddMessage(ChatMessage message)
    {
        Guard.Ensure(message is not null, "Message is required.");
        Messages.Add(message);
    }
}
