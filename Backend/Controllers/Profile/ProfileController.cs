using System.Security.Claims;
using Backend.Services.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TripPlanner.Api.Data;
using TripPlanner.Api.Domain;
using TripPlanner.Api.Domain.Events;

namespace Backend.Controllers.Profile;

[ApiController]
[Route("api/profile")]
[Authorize]
public sealed class ProfileController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IBlobStorageService _blobStorage;

    public ProfileController(AppDbContext db, IBlobStorageService blobStorage)
    {
        _db = db;
        _blobStorage = blobStorage;
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();

        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == userId.Value, ct);

        if (user is null) return NotFound("User not found.");

        var memberships = await _db.EventMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId.Value && m.Status == MembershipStatus.Active)
            .Select(m => new { m.EventId })
            .ToListAsync(ct);

        var eventIds = memberships.Select(m => m.EventId).Distinct().ToList();
        var events = await _db.Events
            .AsNoTracking()
            .Where(e => eventIds.Contains(e.EventId))
            .Select(e => new { e.EventId, e.Status })
            .ToListAsync(ct);

        var organizerEventsCount = await _db.Organizers
            .AsNoTracking()
            .CountAsync(o => o.UserId == userId.Value && o.Status == MembershipStatus.Active, ct);

        var participantEventsCount = await _db.Participants
            .AsNoTracking()
            .CountAsync(p => p.UserId == userId.Value && p.Status == MembershipStatus.Active, ct);

        return Ok(new ProfileDto
        {
            UserId = user.UserId,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            ProfilePictureUrl = user.ProfilePictureUrl,
            EmailVerified = user.EmailVerified,
            CreatedAt = user.CreatedAt,
            TotalEvents = events.Count,
            OrganizerEvents = organizerEventsCount,
            ParticipantEvents = participantEventsCount,
            LiveEvents = events.Count(e => e.Status == EventStatus.Active)
        });
    }

    [HttpPut("me")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UpdateMe([FromForm] UpdateProfileRequest req, CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();

        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId.Value, ct);
        if (user is null) return NotFound("User not found.");

        if (!string.IsNullOrWhiteSpace(req.FirstName) || !string.IsNullOrWhiteSpace(req.LastName))
        {
            user.UpdateProfile(
                string.IsNullOrWhiteSpace(req.FirstName) ? user.FirstName : req.FirstName,
                string.IsNullOrWhiteSpace(req.LastName) ? user.LastName : req.LastName);
        }

        if (req.ProfilePicture is not null)
        {
            var url = await _blobStorage.UploadImageAsync(req.ProfilePicture, "profiles", ct);
            user.UpdateProfilePicture(url);
        }

        await _db.SaveChangesAsync(ct);
        return Ok(new
        {
            user.UserId,
            user.FirstName,
            user.LastName,
            user.Email,
            user.ProfilePictureUrl
        });
    }

    [HttpGet("organizer-events")]
    public async Task<IActionResult> OrganizerEvents(CancellationToken ct)
    {
        var userId = GetUserIdFromClaims();
        if (userId is null) return Unauthorized();

        var events = await _db.Events
            .AsNoTracking()
            .Where(e => e.Organizers.Any(o => o.UserId == userId.Value && o.Status == MembershipStatus.Active))
            .Select(e => new OrganizerEventPortfolioDto
            {
                EventId = e.EventId,
                Title = e.Title,
                EventType = e.EventType,
                DestinationName = e.DestinationName,
                StartDate = e.StartDate,
                EndDate = e.EndDate,
                Status = e.Status,
                ThumbnailUrl = e.ThumbnailUrl,
                CreatedAt = e.CreatedAt,
                Role = e.Organizers.Any(o => o.UserId == userId.Value && o.EventMemberId == e.OwnerOrganizerId) ? "Owner" : "Organizer",
                OrganizersCount = e.Organizers.Count(o => o.Status == MembershipStatus.Active),
                ParticipantsCount = e.Participants.Count(p => p.Status == MembershipStatus.Active),
                DaysCount = e.EventDays.Count,
                ActivitiesCount = e.EventDays.SelectMany(d => d.Activities).Count(),
                MediaCount = e.EventMedia.Count
            })
            .OrderByDescending(e => e.StartDate)
            .ToListAsync(ct);

        return Ok(events);
    }

    private Guid? GetUserIdFromClaims()
    {
        var uid = User.FindFirstValue("uid");
        return Guid.TryParse(uid, out var userId) ? userId : null;
    }
}

public sealed class UpdateProfileRequest
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public IFormFile? ProfilePicture { get; set; }
}

public sealed class ProfileDto
{
    public Guid UserId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? ProfilePictureUrl { get; set; }
    public bool EmailVerified { get; set; }
    public DateTime CreatedAt { get; set; }
    public int TotalEvents { get; set; }
    public int OrganizerEvents { get; set; }
    public int ParticipantEvents { get; set; }
    public int LiveEvents { get; set; }
}

public sealed class OrganizerEventPortfolioDto
{
    public Guid EventId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string DestinationName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public EventStatus Status { get; set; }
    public string? ThumbnailUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Role { get; set; } = string.Empty;
    public int OrganizersCount { get; set; }
    public int ParticipantsCount { get; set; }
    public int DaysCount { get; set; }
    public int ActivitiesCount { get; set; }
    public int MediaCount { get; set; }
}
