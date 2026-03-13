using System.ComponentModel.DataAnnotations;
using TripPlanner.Api.Common;

namespace TripPlanner.Api.Domain.Media;

public sealed class EventMedia
{
    private EventMedia() { } // EF

    [Key]
    public Guid MediaId { get; private set; }

    public Guid EventId { get; private set; }
    public Guid UploadedByEventMemberId { get; private set; }

    [MaxLength(40)]
    public string MediaType { get; private set; } = null!;

    [MaxLength(2048)]
    public string FileUrl { get; private set; } = null!;

    public DateTime UploadedAt { get; private set; }

    public EventMedia(Guid eventId, Guid uploaderMemberId, string mediaType, string fileUrl)
    {
        MediaId = Guid.NewGuid();
        EventId = Guard.NotEmpty(eventId, nameof(eventId));
        UploadedByEventMemberId = Guard.NotEmpty(uploaderMemberId, nameof(uploaderMemberId));
        MediaType = Guard.Required(mediaType, nameof(MediaType), 40);
        FileUrl = Guard.Required(fileUrl, nameof(FileUrl), 2048);
        UploadedAt = DateTime.UtcNow;
    }
}
