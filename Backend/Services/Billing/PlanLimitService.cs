using Microsoft.EntityFrameworkCore;
using TripPlanner.Api.Common;
using TripPlanner.Api.Data;
using TripPlanner.Api.Domain;
using TripPlanner.Api.Domain.Users;

namespace Backend.Services.Billing;

public sealed record PlanLimits(
    SubscriptionPlan Plan,
    string Name,
    int EventsPerMonth,
    int ParticipantsPerEvent,
    bool VoiceEnabled,
    bool DriverCallsEnabled);

public sealed class PlanLimitService
{
    public const string PlusProductId = "tripmate_plus_monthly";

    public static readonly PlanLimits Free = new(
        SubscriptionPlan.Free,
        "Free",
        EventsPerMonth: 3,
        ParticipantsPerEvent: 10,
        VoiceEnabled: false,
        DriverCallsEnabled: false);

    public static readonly PlanLimits Plus = new(
        SubscriptionPlan.Plus,
        "TripMate Plus",
        EventsPerMonth: 20,
        ParticipantsPerEvent: 100,
        VoiceEnabled: true,
        DriverCallsEnabled: true);

    public PlanLimits GetLimits(User user)
        => IsPlusActive(user) ? Plus : Free;

    public bool IsPlusActive(User user)
        => user.SubscriptionPlan == SubscriptionPlan.Plus &&
           (user.PlusExpiresAtUtc is null || user.PlusExpiresAtUtc > DateTime.UtcNow);

    public async Task<BillingSnapshot> GetSnapshot(AppDbContext db, Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.UserId == userId, ct);
        var limits = GetLimits(user);
        var monthStart = MonthStartUtc(DateTime.UtcNow);
        var usedEvents = await CountPublishedEventsThisMonth(db, userId, monthStart, ct);

        return new BillingSnapshot
        {
            Plan = limits.Plan.ToString(),
            PlanName = limits.Name,
            PlusExpiresAtUtc = user.PlusExpiresAtUtc,
            EventsPerMonth = limits.EventsPerMonth,
            EventsUsedThisMonth = usedEvents,
            EventsRemainingThisMonth = Math.Max(0, limits.EventsPerMonth - usedEvents),
            ParticipantsPerEvent = limits.ParticipantsPerEvent,
            VoiceEnabled = limits.VoiceEnabled,
            DriverCallsEnabled = limits.DriverCallsEnabled,
            PlusProductId = PlusProductId
        };
    }

    public async Task EnsureCanPublishEvent(AppDbContext db, Guid ownerUserId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstAsync(u => u.UserId == ownerUserId, ct);
        var limits = GetLimits(user);
        var usedEvents = await CountPublishedEventsThisMonth(db, ownerUserId, MonthStartUtc(DateTime.UtcNow), ct);

        Guard.Ensure(
            usedEvents < limits.EventsPerMonth,
            $"Your {limits.Name} plan allows {limits.EventsPerMonth} events per month. Upgrade to TripMate Plus for 20 events per month.");
    }

    public async Task EnsureParticipantCapacity(AppDbContext db, Guid eventId, CancellationToken ct)
    {
        var ownerUserId = await GetEventOwnerUserId(db, eventId, ct);
        var owner = await db.Users.AsNoTracking().FirstAsync(u => u.UserId == ownerUserId, ct);
        var limits = GetLimits(owner);
        var activeParticipants = await db.Participants
            .AsNoTracking()
            .CountAsync(p => p.EventId == eventId && p.Status == MembershipStatus.Active, ct);

        Guard.Ensure(
            activeParticipants < limits.ParticipantsPerEvent,
            $"This event reached the {limits.ParticipantsPerEvent} participant limit for the owner's {limits.Name} plan.");
    }

    public async Task EnsureEventVoiceAllowed(AppDbContext db, Guid eventId, CancellationToken ct)
    {
        var ownerUserId = await GetEventOwnerUserId(db, eventId, ct);
        var owner = await db.Users.AsNoTracking().FirstAsync(u => u.UserId == ownerUserId, ct);
        var limits = GetLimits(owner);

        Guard.Ensure(limits.VoiceEnabled, "Walkie-talkie is available with TripMate Plus.");
    }

    public async Task EnsureDriverCallsAllowed(AppDbContext db, Guid eventId, CancellationToken ct)
    {
        var ownerUserId = await GetEventOwnerUserId(db, eventId, ct);
        var owner = await db.Users.AsNoTracking().FirstAsync(u => u.UserId == ownerUserId, ct);
        var limits = GetLimits(owner);

        Guard.Ensure(limits.DriverCallsEnabled, "Driver calls are available with TripMate Plus.");
    }

    private static async Task<int> CountPublishedEventsThisMonth(AppDbContext db, Guid ownerUserId, DateTime monthStartUtc, CancellationToken ct)
        => await db.Events
            .AsNoTracking()
            .Where(e => e.OwnerOrganizerId != null &&
                        e.PublishedAtUtc != null &&
                        e.PublishedAtUtc >= monthStartUtc &&
                        e.Status != EventStatus.Draft &&
                        e.Organizers.Any(o => o.EventMemberId == e.OwnerOrganizerId && o.UserId == ownerUserId))
            .CountAsync(ct);

    private static async Task<Guid> GetEventOwnerUserId(AppDbContext db, Guid eventId, CancellationToken ct)
    {
        var ownerUserId = await db.Events
            .AsNoTracking()
            .Where(e => e.EventId == eventId && e.OwnerOrganizerId != null)
            .Select(e => e.Organizers
                .Where(o => o.EventMemberId == e.OwnerOrganizerId)
                .Select(o => (Guid?)o.UserId)
                .FirstOrDefault())
            .FirstOrDefaultAsync(ct);

        Guard.Ensure(ownerUserId is not null, "Event owner was not found.");
        return ownerUserId.Value;
    }

    private static DateTime MonthStartUtc(DateTime nowUtc)
        => new(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
}

public sealed class BillingSnapshot
{
    public string Plan { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public DateTime? PlusExpiresAtUtc { get; set; }
    public int EventsPerMonth { get; set; }
    public int EventsUsedThisMonth { get; set; }
    public int EventsRemainingThisMonth { get; set; }
    public int ParticipantsPerEvent { get; set; }
    public bool VoiceEnabled { get; set; }
    public bool DriverCallsEnabled { get; set; }
    public string PlusProductId { get; set; } = string.Empty;
}
