namespace TripPlanner.Api.Domain.Users;

public sealed class DevicePushToken
{
    private DevicePushToken() { }

    public Guid DevicePushTokenId { get; private set; }
    public Guid UserId { get; private set; }
    public string Token { get; private set; } = string.Empty;
    public string Platform { get; private set; } = string.Empty;
    public string? DeviceId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime LastSeenAtUtc { get; private set; }
    public bool IsActive { get; private set; }

    public DevicePushToken(Guid userId, string token, string platform, string? deviceId)
    {
        DevicePushTokenId = Guid.NewGuid();
        UserId = userId;
        Token = token.Trim();
        Platform = platform.Trim();
        DeviceId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId.Trim();
        CreatedAtUtc = DateTime.UtcNow;
        LastSeenAtUtc = CreatedAtUtc;
        IsActive = true;
    }

    public void Refresh(string platform, string? deviceId)
    {
        Platform = platform.Trim();
        DeviceId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId.Trim();
        LastSeenAtUtc = DateTime.UtcNow;
        IsActive = true;
    }

    public void Deactivate()
    {
        LastSeenAtUtc = DateTime.UtcNow;
        IsActive = false;
    }
}
