using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Backend.Services.Crypto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TripPlanner.Api.Data;
using TripPlanner.Api.Domain;
using TripPlanner.Api.Domain.Events;

namespace Backend.Controllers.EventCreation;

[ApiController]
[Route("api/events")]
[Authorize]
public sealed class EventJoinController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IJoinPasswordCryptoService _crypto;

    public EventJoinController(AppDbContext db, IJoinPasswordCryptoService crypto)
    {
        _db = db;
        _crypto = crypto;
    }

    [HttpPost("join")]
    public async Task<IActionResult> Join([FromBody] JoinEventRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var userId = GetUserIdFromClaims();
        if (userId == null)
            return Unauthorized("Invalid user claims.");

        var ev = await _db.Events
            .Include(e => e.Participants)
            .Include(e => e.Organizers)
            .FirstOrDefaultAsync(e => e.JoinCode == req.JoinCode, ct);

        if (ev is null)
            return NotFound("Event not found.");

        if (!ev.IsJoinEnabled)
            return BadRequest("Joining this event is disabled.");

        string decryptedPassword;
        try
        {
            decryptedPassword = _crypto.Decrypt(ev.JoinPasswordEncrypted);
        }
        catch (Exception)
        {
            return StatusCode(500, "Unable to verify join credentials.");
        }

        if (!string.Equals(decryptedPassword, req.JoinPassword, StringComparison.Ordinal))
            return Unauthorized("Invalid join password.");

        ev.JoinWithCredentials(userId.Value, req.JoinPassword, req.Mode);

        await _db.SaveChangesAsync(ct);

        var participant = ev.Participants.FirstOrDefault(p => p.UserId == userId.Value && p.Status == MembershipStatus.Active);

        return Ok(new JoinEventResponse
        {
            EventId = ev.EventId,
            ParticipantId = participant?.EventMemberId ?? Guid.Empty,
            Mode = participant?.Mode ?? req.Mode
        });
    }


    [HttpGet("{eventId}/join-credentials")]
    public async Task<IActionResult> GetJoinCredentials(Guid eventId)
    {
        var userId = GetUserIdFromClaims();
        if (userId == null)
            return Unauthorized();

        var ev = await _db.Events
            .Include(e => e.Organizers)
            .FirstOrDefaultAsync(e => e.EventId == eventId);

        if (ev == null)
            return NotFound();

        var isOrganizer = ev.Organizers.Any(o => o.UserId == userId && o.Status == MembershipStatus.Active);
        if (!isOrganizer)
            return Forbid();

        var password = _crypto.Decrypt(ev.JoinPasswordEncrypted);

        return Ok(new
        {
            ev.EventId,
            ev.JoinCode,
            JoinPassword = password
        });
    }

    private Guid? GetUserIdFromClaims()
    {
        var uid = User.FindFirstValue("uid");
        return Guid.TryParse(uid, out var userId) ? userId : null;
    }
}

public sealed class JoinEventRequest
{
    [Required]
    [StringLength(12, MinimumLength = 6)]
    public string JoinCode { get; set; } = string.Empty;

    [Required]
    public string JoinPassword { get; set; } = string.Empty;

    public ParticipantMode Mode { get; set; } = ParticipantMode.Active;
}

public sealed class JoinEventResponse
{
    public Guid EventId { get; set; }
    public Guid ParticipantId { get; set; }
    public ParticipantMode Mode { get; set; }
}
