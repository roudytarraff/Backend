using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Backend.Services.Storage;

public sealed class AzureBlobStorageService : IBlobStorageService
{
    private readonly BlobContainerClient _container;

    public AzureBlobStorageService(IConfiguration config)
    {
        var connectionString = config["AzureBlob:ConnectionString"];
        var containerName = config["AzureBlob:ContainerName"];

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("AzureBlob:ConnectionString is missing.");

        if (string.IsNullOrWhiteSpace(containerName))
            throw new InvalidOperationException("AzureBlob:ContainerName is missing.");

        var serviceClient = new BlobServiceClient(connectionString);
        _container = serviceClient.GetBlobContainerClient(containerName);

        _container.CreateIfNotExists(PublicAccessType.Blob);
    }

    public async Task<string> UploadImageAsync(IFormFile file, string folder, CancellationToken ct)
    {
        ValidateImage(file);

        var extension = Path.GetExtension(file.FileName);
        var fileName = $"{folder}/{Guid.NewGuid()}{extension}";
        var blobClient = _container.GetBlobClient(fileName);

        await using var stream = file.OpenReadStream();

        await blobClient.UploadAsync(
            stream,
            new BlobHttpHeaders
            {
                ContentType = file.ContentType
            },
            cancellationToken: ct
        );

        return blobClient.Uri.ToString();
    }

    private static void ValidateImage(IFormFile file)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file));

        if (file.Length == 0)
            throw new ArgumentException("Empty file.");

        if (file.Length > 5 * 1024 * 1024)
            throw new ArgumentException("Image too large. Max 5 MB.");

        if (string.IsNullOrWhiteSpace(file.ContentType) ||
            !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only image files are allowed.");
        }
    }
}