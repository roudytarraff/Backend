using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Backend.Services.Billing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TripPlanner.Api.Data;

namespace Backend.Controllers.Billing;

[ApiController]
[Route("api/billing")]
[Authorize]
public sealed class BillingController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly PlanLimitService _plans;
    private readonly StorePurchaseValidationService _storeValidation;
    private readonly BillingStoreOptions _billingOptions;

    public BillingController(
        AppDbContext db,
        PlanLimitService plans,
        StorePurchaseValidationService storeValidation,
        Microsoft.Extensions.Options.IOptions<BillingStoreOptions> billingOptions)
    {
        _db = db;
        _plans = plans;
        _storeValidation = storeValidation;
        _billingOptions = billingOptions.Value;
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();

        return Ok(await _plans.GetSnapshot(_db, userId.Value, ct));
    }

    [HttpGet("plans")]
    public IActionResult Plans()
        => Ok(new
        {
            Products = new object[]
            {
                new
                {
                    Plan = "Free",
                    Price = "$0",
                    EventsPerMonth = PlanLimitService.Free.EventsPerMonth,
                    ParticipantsPerEvent = PlanLimitService.Free.ParticipantsPerEvent,
                    Features = new[]
                    {
                        "Itinerary planning",
                        "Maps and directions",
                        "Event chat",
                        "Live location sharing",
                        "Driver assignment and driver location",
                        "All phone notifications"
                    }
                },
                new
                {
                    Plan = "TripMate Plus",
                    ProductId = PlanLimitService.PlusProductId,
                    Price = "Store price",
                    EventsPerMonth = PlanLimitService.Plus.EventsPerMonth,
                    ParticipantsPerEvent = PlanLimitService.Plus.ParticipantsPerEvent,
                    Features = new[]
                    {
                        "Everything in Free",
                        "20 events per month",
                        "100 participants per event",
                        "Walkie-talkie",
                        "Driver calls and driver chat"
                    }
                }
            }
        });

    [HttpPost("confirm-plus")]
    public async Task<IActionResult> ConfirmPlus([FromBody] ConfirmPlusPurchaseRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();

        if (!string.Equals(req.ProductId, PlanLimitService.PlusProductId, StringComparison.Ordinal))
            return BadRequest("Unknown product.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId.Value, ct);
        if (user is null) return Unauthorized();

        var validation = _billingOptions.RequireStoreValidation
            ? await _storeValidation.ValidatePlusPurchase(req, ct)
            : _storeValidation.CreateDevelopmentResult(req);

        if (validation is null) return BadRequest("Purchase could not be validated.");

        user.ActivatePlus(
            validation.Platform,
            validation.ProductId,
            validation.TransactionId,
            validation.ExpiresAtUtc);

        await _db.SaveChangesAsync(ct);

        return Ok(await _plans.GetSnapshot(_db, user.UserId, ct));
    }

    private Guid? GetUserIdFromClaims()
    {
        var uid = User.FindFirstValue("uid");
        return Guid.TryParse(uid, out var userId) ? userId : null;
    }
}

public sealed class ConfirmPlusPurchaseRequest
{
    [Required]
    [MaxLength(30)]
    public string Platform { get; set; } = string.Empty;

    [Required]
    [MaxLength(120)]
    public string ProductId { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? TransactionId { get; set; }

    [MaxLength(2048)]
    public string? PurchaseToken { get; set; }

    public string? Receipt { get; set; }
}
