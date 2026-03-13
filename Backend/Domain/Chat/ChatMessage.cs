using System.ComponentModel.DataAnnotations;
using TripPlanner.Api.Common;

namespace TripPlanner.Api.Domain.Chat;

public sealed class ChatMessage
{
    private ChatMessage() { } // EF

    [Key]
    public Guid ChatMessageId { get; private set; }

    public Guid ChatRoomId { get; private set; }
    public Guid SentByEventMemberId { get; private set; }

    [MaxLength(2000)]
    public string Content { get; private set; } = null!;

    public DateTime SentAt { get; private set; }
    public bool IsDeleted { get; private set; }

    public ChatMessage(Guid chatRoomId, Guid senderMemberId, string content)
    {
        ChatMessageId = Guid.NewGuid();
        ChatRoomId = Guard.NotEmpty(chatRoomId, nameof(chatRoomId));
        SentByEventMemberId = Guard.NotEmpty(senderMemberId, nameof(senderMemberId));
        Content = Guard.Required(content, nameof(Content), 2000);
        SentAt = DateTime.UtcNow;
        IsDeleted = false;
    }

    public void Delete() => IsDeleted = true;
}
