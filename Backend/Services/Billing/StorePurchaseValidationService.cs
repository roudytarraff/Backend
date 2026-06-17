using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Backend.Controllers.Billing;
using Google.Apis.AndroidPublisher.v3;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;
using TripPlanner.Api.Common;

namespace Backend.Services.Billing;

public sealed record StorePurchaseValidationResult(
    string Platform,
    string ProductId,
    string TransactionId,
    DateTime ExpiresAtUtc);

public sealed class StorePurchaseValidationService
{
    private readonly BillingStoreOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<StorePurchaseValidationService> _logger;

    public StorePurchaseValidationService(
        IOptions<BillingStoreOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<StorePurchaseValidationService> logger)
    {
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<StorePurchaseValidationResult?> ValidatePlusPurchase(
        ConfirmPlusPurchaseRequest req,
        CancellationToken ct)
    {
        var platform = (req.Platform ?? string.Empty).Trim().ToLowerInvariant();
        var productId = (req.ProductId ?? string.Empty).Trim();
        var expectedProductId = string.IsNullOrWhiteSpace(_options.PlusProductId)
            ? PlanLimitService.PlusProductId
            : _options.PlusProductId.Trim();

        Guard.Ensure(productId == expectedProductId, "Unknown product.");

        if (platform is "android" or "google" or "googleplay")
        {
            return await ValidateGooglePlay(req, expectedProductId, ct);
        }

        if (platform is "ios" or "apple" or "appstore")
        {
            return await ValidateApple(req, expectedProductId, ct);
        }

        Guard.Ensure(false, "Unsupported billing platform.");
        return null;
    }

    public StorePurchaseValidationResult CreateDevelopmentResult(ConfirmPlusPurchaseRequest req)
    {
        var transactionId = !string.IsNullOrWhiteSpace(req.TransactionId)
            ? req.TransactionId.Trim()
            : !string.IsNullOrWhiteSpace(req.PurchaseToken)
                ? req.PurchaseToken.Trim()
                : Guid.NewGuid().ToString("N");

        return new StorePurchaseValidationResult(
            req.Platform.Trim().ToLowerInvariant(),
            req.ProductId.Trim(),
            transactionId,
            DateTime.UtcNow.AddMonths(1));
    }

    private async Task<StorePurchaseValidationResult> ValidateGooglePlay(
        ConfirmPlusPurchaseRequest req,
        string expectedProductId,
        CancellationToken ct)
    {
        var packageName = _options.GooglePlay.PackageName.Trim();
        var serviceAccountJson = GetGoogleServiceAccountJson();
        var token = req.PurchaseToken?.Trim();

        Guard.Ensure(!string.IsNullOrWhiteSpace(packageName), "Google Play package name is missing.");
        Guard.Ensure(!string.IsNullOrWhiteSpace(serviceAccountJson), "Google Play service account is missing.");
        Guard.Ensure(!string.IsNullOrWhiteSpace(token), "Google Play purchase token is missing.");

        var credential = GoogleCredential
            .FromJson(serviceAccountJson)
            .CreateScoped(AndroidPublisherService.Scope.Androidpublisher);

        using var service = new AndroidPublisherService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "TripConnect"
        });

        var purchase = await service.Purchases.Subscriptionsv2.Get(packageName, token).ExecuteAsync(ct);
        var line = purchase.LineItems?.FirstOrDefault(item =>
            string.Equals(item.ProductId, expectedProductId, StringComparison.Ordinal));

        Guard.Ensure(line is not null, "Google Play product does not match TripConnect Plus.");
        Guard.Ensure(purchase.SubscriptionState is "SUBSCRIPTION_STATE_ACTIVE" or "SUBSCRIPTION_STATE_IN_GRACE_PERIOD",
            "Google Play subscription is not active.");

        var expiry = ParseGoogleExpiry(line!.ExpiryTime);
        Guard.Ensure(expiry > DateTime.UtcNow, "Google Play subscription has expired.");

        return new StorePurchaseValidationResult(
            "android",
            expectedProductId,
            token!,
            expiry);
    }

    private async Task<StorePurchaseValidationResult> ValidateApple(
        ConfirmPlusPurchaseRequest req,
        string expectedProductId,
        CancellationToken ct)
    {
        var transactionId = req.TransactionId?.Trim();
        Guard.Ensure(!string.IsNullOrWhiteSpace(transactionId), "Apple transaction id is missing.");

        var signedTransaction = await FetchAppleSignedTransaction(transactionId!, ct);
        var payload = DecodeJwtPayload(signedTransaction);

        var productId = payload.TryGetProperty("productId", out var productElement)
            ? productElement.GetString()
            : null;
        var bundleId = payload.TryGetProperty("bundleId", out var bundleElement)
            ? bundleElement.GetString()
            : null;
        var originalTransactionId = payload.TryGetProperty("originalTransactionId", out var originalElement)
            ? originalElement.GetString()
            : transactionId;

        Guard.Ensure(productId == expectedProductId, "Apple product does not match TripConnect Plus.");
        if (!string.IsNullOrWhiteSpace(_options.Apple.BundleId))
        {
            Guard.Ensure(bundleId == _options.Apple.BundleId.Trim(), "Apple bundle id does not match this app.");
        }

        var expiresAt = ReadAppleExpiry(payload);
        Guard.Ensure(expiresAt > DateTime.UtcNow, "Apple subscription has expired.");

        return new StorePurchaseValidationResult(
            "ios",
            expectedProductId,
            originalTransactionId ?? transactionId!,
            expiresAt);
    }

    private string? GetGoogleServiceAccountJson()
    {
        if (!string.IsNullOrWhiteSpace(_options.GooglePlay.ServiceAccountJson))
        {
            return _options.GooglePlay.ServiceAccountJson;
        }

        if (!string.IsNullOrWhiteSpace(_options.GooglePlay.ServiceAccountBase64))
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(_options.GooglePlay.ServiceAccountBase64));
        }

        return null;
    }

    private async Task<string> FetchAppleSignedTransaction(string transactionId, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient();
        var host = _options.Apple.UseSandbox
            ? "https://api.storekit-sandbox.itunes.apple.com"
            : "https://api.storekit.itunes.apple.com";
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{host}/inApps/v1/transactions/{Uri.EscapeDataString(transactionId)}");

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateAppleJwt());

        using var response = await client.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Apple Store Server API validation failed: {Status} {Body}", response.StatusCode, body);
            Guard.Ensure(false, "Apple purchase could not be validated.");
        }

        using var json = JsonDocument.Parse(body);
        if (json.RootElement.TryGetProperty("signedTransactionInfo", out var signed))
        {
            return signed.GetString() ?? string.Empty;
        }

        Guard.Ensure(false, "Apple response did not include signed transaction info.");
        return string.Empty;
    }

    private string CreateAppleJwt()
    {
        var issuerId = _options.Apple.IssuerId.Trim();
        var keyId = _options.Apple.KeyId.Trim();
        var bundleId = _options.Apple.BundleId.Trim();
        var privateKey = GetApplePrivateKeyPem();

        Guard.Ensure(!string.IsNullOrWhiteSpace(issuerId), "Apple issuer id is missing.");
        Guard.Ensure(!string.IsNullOrWhiteSpace(keyId), "Apple key id is missing.");
        Guard.Ensure(!string.IsNullOrWhiteSpace(bundleId), "Apple bundle id is missing.");
        Guard.Ensure(!string.IsNullOrWhiteSpace(privateKey), "Apple private key is missing.");

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privateKey);

        var key = new ECDsaSecurityKey(ecdsa) { KeyId = keyId };
        var credentials = new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256);
        var now = DateTime.UtcNow;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuerId,
            Audience = "appstoreconnect-v1",
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(15),
            Claims = new Dictionary<string, object>
            {
                ["bid"] = bundleId
            },
            SigningCredentials = credentials
        };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateJwtSecurityToken(descriptor);
        token.Header["kid"] = keyId;

        return handler.WriteToken(token);
    }

    private string? GetApplePrivateKeyPem()
    {
        if (!string.IsNullOrWhiteSpace(_options.Apple.PrivateKeyPem))
        {
            return _options.Apple.PrivateKeyPem.Replace("\\n", "\n");
        }

        if (!string.IsNullOrWhiteSpace(_options.Apple.PrivateKeyBase64))
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(_options.Apple.PrivateKeyBase64));
        }

        return null;
    }

    private static JsonElement DecodeJwtPayload(string jwt)
    {
        var parts = jwt.Split('.');
        Guard.Ensure(parts.Length >= 2, "Store transaction payload is invalid.");
        var bytes = Base64UrlDecode(parts[1]);
        return JsonDocument.Parse(bytes).RootElement.Clone();
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var output = value.Replace('-', '+').Replace('_', '/');
        switch (output.Length % 4)
        {
            case 2:
                output += "==";
                break;
            case 3:
                output += "=";
                break;
        }
        return Convert.FromBase64String(output);
    }

    private static DateTime ParseGoogleExpiry(object? value)
    {
        if (value is null) return DateTime.MinValue;
        if (DateTime.TryParse(value.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return parsed.ToUniversalTime();
        }
        return DateTime.MinValue;
    }

    private static DateTime ReadAppleExpiry(JsonElement payload)
    {
        if (!payload.TryGetProperty("expiresDate", out var expiresElement))
        {
            return DateTime.MinValue;
        }

        if (expiresElement.ValueKind == JsonValueKind.Number && expiresElement.TryGetInt64(out var ms))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime;
        }

        if (long.TryParse(expiresElement.GetString(), out var textMs))
        {
            return DateTimeOffset.FromUnixTimeMilliseconds(textMs).UtcDateTime;
        }

        return DateTime.MinValue;
    }
}
