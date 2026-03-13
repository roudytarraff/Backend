using System.ComponentModel.DataAnnotations;
using TripPlanner.Api.Common;

namespace TripPlanner.Api.Domain.Voice;

public sealed class VoiceRecording
{
    private VoiceRecording() { } // EF

    [Key]
    public Guid VoiceRecordingId { get; private set; }

    public Guid VoiceSessionId { get; private set; }

    [MaxLength(2048)]
    public string FileUrl { get; private set; } = null!;

    public int DurationSeconds { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public VoiceRecording(Guid voiceSessionId, string fileUrl, int durationSeconds)
    {
        VoiceRecordingId = Guid.NewGuid();
        VoiceSessionId = Guard.NotEmpty(voiceSessionId, nameof(voiceSessionId));
        FileUrl = Guard.Required(fileUrl, nameof(FileUrl), 2048);
        DurationSeconds = Guard.NonNegative(durationSeconds, nameof(DurationSeconds));
        CreatedAt = DateTime.UtcNow;
    }
}
