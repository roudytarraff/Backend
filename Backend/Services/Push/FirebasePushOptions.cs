namespace Backend.Services.Push;

public sealed class FirebasePushOptions
{
    public string? ServiceAccountJson { get; set; }
    public string? ServiceAccountBase64 { get; set; }
    public string? ProjectId { get; set; }
}
