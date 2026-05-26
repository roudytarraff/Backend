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

    public BillingController(AppDbContext db, PlanLimitService plans)
    {
        _db = db;
        _plans = plans;
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

        var transactionId = !string.IsNullOrWhiteSpace(req.TransactionId)
            ? req.TransactionId.Trim()
            : !string.IsNullOrWhiteSpace(req.PurchaseToken)
                ? req.PurchaseToken.Trim()
                : Guid.NewGuid().ToString("N");

        // The mobile stores still own the real subscription status. This unlocks the user
        // after the native store reports success; proper server receipt validation can be
        // added when App Store / Play service credentials are available.
        user.ActivatePlus(
            req.Platform.Trim().ToLowerInvariant(),
            req.ProductId.Trim(),
            transactionId,
            DateTime.UtcNow.AddMonths(1));

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
