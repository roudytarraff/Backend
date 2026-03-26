namespace Backend.Services.Storage;

public interface IBlobStorageService
{
    Task<string> UploadImageAsync(IFormFile file, string folder, CancellationToken ct);
}