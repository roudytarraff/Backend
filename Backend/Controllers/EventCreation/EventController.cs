using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Backend.Services.Crypto;
using Backend.Services.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TripPlanner.Api.Data;
using TripPlanner.Api.Domain.Events;
using TripPlanner.Api.Domain.Schedule;

namespace Backend.Controllers.EventCreation;

[ApiController]
[Route("api/events")]
[Authorize]
public class EventController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IJoinPasswordCryptoService _crypto;
    private readonly IBlobStorageService _blobStorage;

    public EventController(
        AppDbContext db,
        IJoinPasswordCryptoService crypto,
        IBlobStorageService blobStorage)
    {
        _db = db;
        _crypto = crypto;
        _blobStorage = blobStorage;
    }

    [HttpPost]
[Consumes("multipart/form-data")]
public async Task<IActionResult> Create([FromForm] CreateEventMultipartRequest form, CancellationToken ct)
{
    var userId = Guid.Parse(User.FindFirstValue("uid")!);

    if (string.IsNullOrWhiteSpace(form.Data))
        return BadRequest("Missing data payload.");

    var req = JsonSerializer.Deserialize<CreateEventRequest>(
        form.Data,
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

    if (req is null)
        return BadRequest("Invalid data payload.");

    var filesByKey = Request.Form.Files.ToDictionary(f => f.Name, f => f);

    string? eventThumbnailUrl = null;

    if (!string.IsNullOrWhiteSpace(req.ThumbnailFileKey) &&
        filesByKey.TryGetValue(req.ThumbnailFileKey, out var eventThumbnailFile))
    {
        eventThumbnailUrl = await _blobStorage.UploadImageAsync(eventThumbnailFile, "events", ct);
    }

    var startDateUtc = DateTime.SpecifyKind(req.StartDate, DateTimeKind.Utc);
    var joinCode = await GenerateUniqueJoinCodeAsync(11, ct);
    var encryptedPassword = _crypto.Encrypt(req.JoinPassword);

    req.Description="hello";
    var ev = new Event(
        req.Title,
        req.Description,
        req.EventType,
        startDateUtc,
        encryptedPassword,
        joinCode
    );

    ev.SetDestination(
        req.DestinationName,
        req.DestinationLatitude,
        req.DestinationLongitude
    );

    _db.Events.Add(ev);
    await _db.SaveChangesAsync(ct);

    var organizer = ev.CreateOwner(userId);
    _db.Organizers.Add(organizer);
    await _db.SaveChangesAsync(ct);

    ev.SetOwnerOrganizerId(organizer.EventMemberId);

    if (!string.IsNullOrWhiteSpace(eventThumbnailUrl))
        ev.UpdateThumbnail(userId, eventThumbnailUrl);

    foreach (var day in req.Days)
    {
        var dayDateUtc = DateTime.SpecifyKind(day.Date, DateTimeKind.Utc);

        var eventDay = ev.AddEventDay(userId, dayDateUtc, day.Title);

        foreach (var act in day.Activities)
        {
            var activity = new Activity(
                eventDay.EventDayId,
                act.Title,
                act.Type,
                act.DurationMinutes,
                act.Order
            );

            activity.UpdateLocation(
                act.LocationName,
                act.Latitude,
                act.Longitude
            );

            if (!string.IsNullOrWhiteSpace(act.ThumbnailFileKey) &&
                filesByKey.TryGetValue(act.ThumbnailFileKey, out var activityThumbnailFile))
            {
                var activityThumbnailUrl =
                    await _blobStorage.UploadImageAsync(activityThumbnailFile, "activities", ct);

                activity.UpdateThumbnail(activityThumbnailUrl);
            }

            eventDay.AddActivity(activity);

            foreach (var step in act.Steps)
            {
                var s = new ActivityStep(
                    activity.ActivityId,
                    step.StepOrder,
                    step.Description,
                    step.IsMandatory
                );

                activity.AddStep(s);
            }
        }
    }

    await _db.SaveChangesAsync(ct);

    return Ok(new
    {
        ev.EventId,
        ev.JoinCode,
        JoinPassword = _crypto.Decrypt(ev.JoinPasswordEncrypted),
        ev.ThumbnailUrl
    });
}

    [HttpGet("{eventId}")]
    public async Task<IActionResult> GetEvent(Guid eventId, CancellationToken ct)
    {
        var ev = await _db.Events
            .Include(e => e.Organizers)
            .Include(e => e.Participants)
            .Include(e => e.EventDays)
                .ThenInclude(d => d.Activities)
                    .ThenInclude(a => a.Steps)
            .Include(e => e.ChatRoom)
            .Include(e => e.VoiceChannel)
            .FirstOrDefaultAsync(e => e.EventId == eventId, ct);

        if (ev == null)
            return NotFound();

        return Ok(ev);
    }

    private async Task<string> GenerateUniqueJoinCodeAsync(int length, CancellationToken ct)
    {
        if (length < 6 || length > 12)
            throw new ArgumentOutOfRangeException(nameof(length), "JoinCode length must be between 6 and 12.");

        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

        while (true)
        {
            var chars = new char[length];

            for (int i = 0; i < length; i++)
            {
                chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
            }

            var code = new string(chars);

            var exists = await _db.Events.AnyAsync(e => e.JoinCode == code, ct);
            if (!exists)
                return code;
        }
    }










    [HttpPost("test-upload")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> TestUpload(IFormFile file, CancellationToken ct)
    {
        if (file == null)
            return BadRequest("No file");

        var url = await _blobStorage.UploadImageAsync(file, "test", ct);

        return Ok(new { url });
}
}