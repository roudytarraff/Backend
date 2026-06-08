using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using SkiaSharp;

namespace Backend.Services.Storage;

public sealed class AzureBlobStorageService : IBlobStorageService
{
    private const int ProfileImageMaxDimension = 512;
    private const int StandardImageMaxDimension = 1280;
    private const int MediaImageMaxDimension = 1600;
    private const int JpegQuality = 82;
    private const string ImageContentType = "image/jpeg";

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

        var maxDimension = folder.Equals("profiles", StringComparison.OrdinalIgnoreCase)
            ? ProfileImageMaxDimension
            : StandardImageMaxDimension;

        return await UploadResizedImageAsync(file, folder, maxDimension, ct);
    }

    public async Task<string> UploadMediaAsync(IFormFile file, string folder, CancellationToken ct)
    {
        ValidateMedia(file);

        if (IsImage(file))
            return await UploadResizedImageAsync(file, folder, MediaImageMaxDimension, ct);

        return await UploadFileAsync(file, folder, ct);
    }

    private async Task<string> UploadFileAsync(IFormFile file, string folder, CancellationToken ct)
    {
        var extension = Path.GetExtension(file.FileName);
        var fileName = $"{folder}/{Guid.NewGuid()}{extension}";
        var blobClient = _container.GetBlobClient(fileName);

        await using var stream = file.OpenReadStream();

        await blobClient.UploadAsync(
            stream,
            new BlobHttpHeaders
            {
                ContentType = file.ContentType,
                CacheControl = "public,max-age=31536000,immutable"
            },
            cancellationToken: ct
        );

        return blobClient.Uri.ToString();
    }

    private async Task<string> UploadResizedImageAsync(IFormFile file, string folder, int maxDimension, CancellationToken ct)
    {
        await using var input = file.OpenReadStream();
        using var original = SKBitmap.Decode(input);

        if (original == null)
            throw new ArgumentException("Unsupported image format.");

        ct.ThrowIfCancellationRequested();

        var largestSide = Math.Max(original.Width, original.Height);
        var scale = largestSide > maxDimension ? (double)maxDimension / largestSide : 1d;
        var width = Math.Max(1, (int)Math.Round(original.Width * scale));
        var height = Math.Max(1, (int)Math.Round(original.Height * scale));

        using var resized = scale < 1d
            ? new SKBitmap(new SKImageInfo(width, height, original.ColorType, original.AlphaType))
            : null;

        if (resized != null && !original.ScalePixels(resized, SKSamplingOptions.Default))
            throw new ArgumentException("Could not process image.");

        using var imageToEncode = SKImage.FromBitmap(resized ?? original);
        using var encoded = imageToEncode.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
        if (encoded == null)
            throw new ArgumentException("Could not encode image.");

        await using var output = new MemoryStream();
        encoded.SaveTo(output);
        output.Position = 0;

        var fileName = $"{folder}/{Guid.NewGuid():N}.jpg";
        var blobClient = _container.GetBlobClient(fileName);

        await blobClient.UploadAsync(
            output,
            new BlobHttpHeaders
            {
                ContentType = ImageContentType,
                CacheControl = "public,max-age=31536000,immutable"
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

    private static void ValidateMedia(IFormFile file)
    {
        if (file == null)
            throw new ArgumentNullException(nameof(file));

        if (file.Length == 0)
            throw new ArgumentException("Empty file.");

        if (file.Length > 50 * 1024 * 1024)
            throw new ArgumentException("Media too large. Max 50 MB.");

        if (string.IsNullOrWhiteSpace(file.ContentType) ||
            (!file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) &&
             !file.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Only image and video files are allowed.");
        }
    }

    private static bool IsImage(IFormFile file)
    {
        return file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    }
}
