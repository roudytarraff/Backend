using System.ComponentModel.DataAnnotations;
using TripPlanner.Api.Common;

namespace TripPlanner.Api.Domain.Voice;

public sealed class VoiceChannel
{
    private VoiceChannel() { } // EF

    [Key]
    public Guid VoiceChannelId { get; private set; }

    public Guid EventId { get; private set; }

    [MaxLength(60)]
    public string Name { get; private set; } = null!;

    public bool IsOpen { get; private set; }

    public List<VoiceSession> Sessions { get; private set; } = new();

    public VoiceChannel(Guid eventId, string name)
    {
        VoiceChannelId = Guid.NewGuid();
        EventId = Guard.NotEmpty(eventId, nameof(eventId));
        Name = Guard.Required(name, nameof(Name), 60);
        IsOpen = false;
    }

    public void Rename(string name) => Name = Guard.Required(name, nameof(Name), 60);
    public void Open() => IsOpen = true;
    public void Close() => IsOpen = false;
}
