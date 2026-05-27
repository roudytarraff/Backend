using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TripPlanner.Api.Data;
using TripPlanner.Api.Domain.Users;

namespace Backend.Controllers.Notifications;

[ApiController]
[Route("api/device-push-tokens")]
[Authorize]
public sealed class DevicePushTokenController : ControllerBase
{
    private readonly AppDbContext _db;

    public DevicePushTokenController(AppDbContext db)
    {
        _db = db;
    }

    [HttpPost]
    public async Task<IActionResult> Register([FromBody] RegisterDevicePushTokenRequest req, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(req.Token)) return BadRequest("Push token is required.");
        if (string.IsNullOrWhiteSpace(req.Platform)) return BadRequest("Platform is required.");

        var token = req.Token.Trim();
        var existing = await _db.DevicePushTokens.FirstOrDefaultAsync(t => t.Token == token, ct);
        if (existing is null)
        {
            _db.DevicePushTokens.Add(new DevicePushToken(userId.Value, token, req.Platform, req.DeviceId));
        }
        else if (existing.UserId == userId.Value)
        {
            existing.Refresh(req.Platform, req.DeviceId);
        }
        else
        {
            existing.Deactivate();
            _db.DevicePushTokens.Add(new DevicePushToken(userId.Value, token, req.Platform, req.DeviceId));
        }

        await _db.SaveChangesAsync(ct);
        return Ok();
    }

    [HttpDelete]
    public async Task<IActionResult> Unregister([FromBody] UnregisterDevicePushTokenRequest req, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(req.Token)) return Ok();

        await _db.DevicePushTokens
            .Where(t => t.UserId == userId.Value && t.Token == req.Token)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.IsActive, false)
                .SetProperty(t => t.LastSeenAtUtc, DateTime.UtcNow), ct);

        return Ok();
    }

    private Guid? GetUserIdFromClaims()
    {
        var uid = User.FindFirstValue("uid");
        return Guid.TryParse(uid, out var userId) ? userId : null;
    }
}

public sealed class RegisterDevicePushTokenRequest
{
    public string Token { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string? DeviceId { get; set; }
}

public sealed class UnregisterDevicePushTokenRequest
{
    public string Token { get; set; } = string.Empty;
}
