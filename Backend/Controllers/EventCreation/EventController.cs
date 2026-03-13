using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
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

    public EventController(AppDbContext db)
    {
        _db = db;
    }

    // ================= CREATE EVENT =================

    [HttpPost]
    public async Task<IActionResult> Create(CreateEventRequest req, CancellationToken ct)
    {
        var userId = Guid.Parse(User.FindFirstValue("uid")!);

        var startDateUtc = DateTime.SpecifyKind(req.StartDate, DateTimeKind.Utc);

        var ev = new Event(
            req.Title,
            req.Description,
            req.EventType,
            startDateUtc,
            req.JoinPassword
            
        );

        // 🔴 FIX: set destination BEFORE saving
        ev.SetDestination(
            req.DestinationName,
            req.DestinationLatitude,
            req.DestinationLongitude
        );

        _db.Events.Add(ev);
        await _db.SaveChangesAsync(ct);

        // create organizer
        var organizer = ev.CreateOwner(userId);
        
        _db.Organizers.Add(organizer);
        await _db.SaveChangesAsync(ct);

        ev.SetOwnerOrganizerId(organizer.EventMemberId);

        await _db.SaveChangesAsync(ct);

        Console.WriteLine("Organizer MemberId: " + organizer.EventMemberId);
Console.WriteLine("Event Organizers: " + string.Join(",", ev.Organizers.Select(o => o.EventMemberId)));

        // ================= EVENT DAYS =================

        foreach (var day in req.Days)
        {
            var dayDateUtc = DateTime.SpecifyKind(day.Date, DateTimeKind.Utc);

            var eventDay = ev.AddEventDay(
                organizer.EventMemberId,
                dayDateUtc,
                day.Title
            );

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

                if (!string.IsNullOrWhiteSpace(act.ThumbnailUrl))
                    activity.UpdateThumbnail(act.ThumbnailUrl);

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
            ev.JoinCode
        });
    }


    // ================= GET FULL EVENT =================

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
}