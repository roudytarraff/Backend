using System.ComponentModel.DataAnnotations;
using TripPlanner.Api.Common;

namespace TripPlanner.Api.Domain.Voice;

public sealed class VoiceSession
{
    private VoiceSession() { } // EF

    [Key]
    public Guid VoiceSessionId { get; private set; }

    public Guid VoiceChannelId { get; private set; }
    public Guid StartedByEventMemberId { get; private set; }

    public DateTime StartedAt { get; private set; }
    public DateTime? EndedAt { get; private set; }

    public bool IsRecorded { get; private set; }

    public List<VoiceRecording> Recordings { get; private set; } = new();

    public VoiceSession(Guid voiceChannelId, Guid startedByEventMemberId, bool isRecorded)
    {
        VoiceSessionId = Guid.NewGuid();
        VoiceChannelId = Guard.NotEmpty(voiceChannelId, nameof(voiceChannelId));
        StartedByEventMemberId = Guard.NotEmpty(startedByEventMemberId, nameof(startedByEventMemberId));
        StartedAt = DateTime.UtcNow;
        IsRecorded = isRecorded;
    }

    public void EndSession() => EndedAt ??= DateTime.UtcNow;

    public void AddRecording(VoiceRecording rec)
    {
        Guard.Ensure(IsRecorded, "Session is not recorded.");
        Guard.Ensure(rec is not null, "Recording is required.");
        Recordings.Add(rec);
    }
}
