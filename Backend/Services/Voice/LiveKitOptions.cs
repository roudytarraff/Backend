namespace Backend.Services.Voice;

public sealed class LiveKitOptions
{
    public string ServerUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ApiSecret { get; set; } = string.Empty;
    public int TokenMinutes { get; set; } = 30;
}
