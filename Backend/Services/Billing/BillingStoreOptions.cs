namespace Backend.Services.Billing;

public sealed class BillingStoreOptions
{
    public bool RequireStoreValidation { get; set; }
    public string PlusProductId { get; set; } = PlanLimitService.PlusProductId;

    public GooglePlayBillingOptions GooglePlay { get; set; } = new();
    public AppleStoreBillingOptions Apple { get; set; } = new();
}

public sealed class GooglePlayBillingOptions
{
    public string PackageName { get; set; } = string.Empty;
    public string? ServiceAccountJson { get; set; }
    public string? ServiceAccountBase64 { get; set; }
}

public sealed class AppleStoreBillingOptions
{
    public string BundleId { get; set; } = string.Empty;
    public string IssuerId { get; set; } = string.Empty;
    public string KeyId { get; set; } = string.Empty;
    public string? PrivateKeyPem { get; set; }
    public string? PrivateKeyBase64 { get; set; }
    public bool UseSandbox { get; set; }
}
