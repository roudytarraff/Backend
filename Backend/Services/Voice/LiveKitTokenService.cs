using System.Text.Json;
using Microsoft.Extensions.Options;
using Livekit.Server.Sdk.Dotnet;

namespace Backend.Services.Voice;

public sealed class LiveKitTokenService
{
    private readonly LiveKitOptions _options;

    public LiveKitTokenService(IOptions<LiveKitOptions> options)
    {
        _options = options.Value;
    }

    public string ServerUrl => _options.ServerUrl;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.ServerUrl) &&
        !string.IsNullOrWhiteSpace(_options.ApiKey) &&
        !string.IsNullOrWhiteSpace(_options.ApiSecret);

    public string CreateJoinToken(string roomName, string identity, string displayName, object metadata)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("LiveKit is not configured.");
        }

        var ttl = TimeSpan.FromMinutes(Math.Max(5, _options.TokenMinutes));

        return new AccessToken(_options.ApiKey, _options.ApiSecret)
            .WithIdentity(identity)
            .WithName(displayName)
            .WithMetadata(JsonSerializer.Serialize(metadata))
            .WithGrants(new VideoGrants
            {
                Room = roomName,
                RoomJoin = true,
                CanPublish = true,
                CanSubscribe = true,
                CanPublishData = true
            })
            .WithTtl(ttl)
            .ToJwt();
    }
}
