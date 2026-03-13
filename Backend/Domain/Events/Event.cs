using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using TripPlanner.Api.Common;
using TripPlanner.Api.Domain.Chat;
using TripPlanner.Api.Domain.Media;
using TripPlanner.Api.Domain.Schedule;
using TripPlanner.Api.Domain.Voice;

namespace TripPlanner.Api.Domain.Events;

public sealed class Event
{
    private Event() { } // EF

    [Key]
    public Guid EventId { get; private set; }

    [MaxLength(120)]
    public string Title { get; private set; } = null!;

    [MaxLength(4000)]
    public string Description { get; private set; } = null!;

    [MaxLength(60)]
    public string EventType { get; private set; } = "General";

    public DateTime StartDate { get; private set; }

    public DateTime? EndDate { get; private set; }

    public EventStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public string DestinationName { get; private set; }

    public double DestinationLatitude { get; private set; }

    public double DestinationLongitude { get; private set; }

    [MaxLength(2048)]
    public string? ThumbnailUrl { get; private set; }

    // Owner is one of Organizers (by EventMemberId)
    public Guid? OwnerOrganizerId { get; private set; }

    public bool IsLocationSharingActive { get; private set; }
    public LocationShareScope LocationShareScope { get; private set; }

    // Stored as jsonb via ModelMap conversion
    public HashSet<Capability> PassiveAllowedCapabilities { get; private set; } = new();

    public List<Organizer> Organizers { get; private set; } = new();
    public List<Participant> Participants { get; private set; } = new();

    // Owned collection (EventLocationGrants)
    public List<LocationGrant> LocationGrants { get; private set; } = new();

    public List<EventDay> EventDays { get; private set; } = new();

    // Exactly 1 per event
    public ChatRoom ChatRoom { get; private set; } = null!;
    public VoiceChannel VoiceChannel { get; private set; } = null!;

    public List<EventMedia> EventMedia { get; private set; } = new();

    // ✅ Join credentials
    [MaxLength(12)]
    public string JoinCode { get; private set; } = null!;

    [MaxLength(500)]
    public string JoinPasswordHash { get; private set; } = null!; // salt+hash in ONE string

    public bool IsJoinEnabled { get; private set; }

    // ✅ ctor: StartDate + JoinPassword (set at creation)
    public Event(
    string title,
    string description,
    string eventType,
    DateTime startDate,
    string joinPassword,
    bool isJoinEnabled = true,
    int joinCodeLength = 6)
{
    EventId = Guid.NewGuid();

    Title = Guard.Required(title, nameof(Title), 120);
    Description = Guard.Required(description, nameof(Description), 4000);
    EventType = Guard.Required(eventType, nameof(EventType), 60);

    StartDate = startDate;
    EndDate = null;

    Status = EventStatus.Draft;
    CreatedAt = DateTime.UtcNow;

    IsLocationSharingActive = false;
    LocationShareScope = LocationShareScope.OrganizersOnly;

    PassiveAllowedCapabilities = new HashSet<Capability>
    {
        Capability.ViewSchedule,
        Capability.ChatRead
    };

    ChatRoom = new ChatRoom(EventId);
    VoiceChannel = new VoiceChannel(EventId, "Voice");

    JoinCode = GenerateJoinCode(joinCodeLength);
    JoinPasswordHash = HashJoinPassword(joinPassword);
    IsJoinEnabled = isJoinEnabled;
}

public void SetOwnerOrganizerId(Guid organizerId)
{
    OwnerOrganizerId = organizerId;
}

    // ---------------- Join via credentials ----------------

    public void JoinWithCredentials(Guid actorUserId, string joinPassword, ParticipantMode mode)
    {
        EnsureOpen();

        Guard.NotEmpty(actorUserId, nameof(actorUserId));
        Guard.Ensure(IsJoinEnabled, "Joining this event is disabled.");

        Guard.Ensure(!IsMember(actorUserId), "User is already a member.");

        Guard.Ensure(VerifyJoinPassword(joinPassword, JoinPasswordHash), "Invalid join credentials.");

        Participants.Add(new Participant(EventId, actorUserId, mode));

        // EndDate might remain null until EventDays exist; that’s fine.
        RecalculateEndDateFromSchedule();
    }

    public void ChangeJoinPassword(Guid actorUserId, string newJoinPassword)
    {
        EnsureOpen();
        RequireOrganizer(actorUserId);

        JoinPasswordHash = HashJoinPassword(newJoinPassword);
    }

    public string RotateJoinCode(Guid actorUserId, int joinCodeLength = 6)
    {
        EnsureOpen();
        RequireOrganizer(actorUserId);

        JoinCode = GenerateJoinCode(joinCodeLength);
        return JoinCode;
    }

    public void EnableJoin(Guid actorUserId)
    {
        EnsureOpen();
        RequireOrganizer(actorUserId);
        IsJoinEnabled = true;
    }

    public void DisableJoin(Guid actorUserId)
    {
        EnsureOpen();
        RequireOrganizer(actorUserId);
        IsJoinEnabled = false;
    }

    // ---------------- Event lifecycle ----------------

    public void UpdateDetails(Guid actorUserId, string title, string description, string eventType)
    {
        EnsureOpen();
        RequireOrganizer(actorUserId);

        Title = Guard.Required(title, nameof(Title), 120);
        Description = Guard.Required(description, nameof(Description), 4000);
        EventType = Guard.Required(eventType, nameof(EventType), 60);
    }

    // ✅ only start date; end date derived from EventDays
    public void Reschedule(Guid actorUserId, DateTime newStartDate)
    {
        EnsureOpen();
        RequireOrganizer(actorUserId);

        StartDate = newStartDate;
        RecalculateEndDateFromSchedule();
    }

    public void UpdateThumbnail(Guid actorUserId, string? url)
    {
        EnsureOpen();
        RequireOrganizer(actorUserId);
        ThumbnailUrl = Guard.UrlOrNull(url, nameof(ThumbnailUrl), 2048);
    }

    public void SetDestination(string name, double lat, double lng)
    {
    DestinationName = Guard.Required(name, nameof(DestinationName), 120);
    DestinationLatitude = lat;
    DestinationLongitude = lng;
    }

    // ✅ no reason
    public void Cancel(Guid actorUserId)
    {
        EnsureOpen();
        RequireOwner(actorUserId);
        Status = EventStatus.Cancelled;
    }

    public void Complete(Guid actorUserId)
    {
        EnsureOpen();
        RequireOwner(actorUserId);
        Status = EventStatus.Completed;
    }

    // ---------------- Organizers / Owner ----------------

    public void AddOrganizer(Guid actorUserId, Guid userId)
    {
        EnsureOpen();
        RequireOwner(actorUserId);

        Guard.NotEmpty(userId, nameof(userId));
        Guard.Ensure(!IsMember(userId), "User is already a member.");

        Organizers.Add(new Organizer(EventId, userId));
    }

    public Organizer CreateOwner(Guid userId)
    {
    Guard.NotEmpty(userId, nameof(userId));

    var organizer = new Organizer(EventId, userId);

    Organizers.Add(organizer);

    OwnerOrganizerId = organizer.EventMemberId;

    return organizer;
    }

    public void RemoveOrganizer(Guid actorUserId, Guid organizerId)
    {
        EnsureOpen();
        RequireOwner(actorUserId);

        var org = Organizers.FirstOrDefault(o => o.EventMemberId == organizerId);
        Guard.Ensure(org is not null, "Organizer not found.");

        Guard.Ensure(org.EventMemberId != OwnerOrganizerId, "Cannot remove owner. Transfer ownership first.");

        Organizers.Remove(org);
        Guard.Ensure(Organizers.Count > 0, "Event must have at least one organizer.");
    }

    public void TransferOwnership(Guid actorUserId, Guid toOrganizerId)
    {
        EnsureOpen();
        RequireOwner(actorUserId);

        var target = Organizers.FirstOrDefault(o => o.EventMemberId == toOrganizerId);
        Guard.Ensure(target is not null, "Target organizer not found.");
        Guard.Ensure(target.Status == MembershipStatus.Active, "Target organizer must be active.");

        OwnerOrganizerId = target.EventMemberId;
    }

    // ---------------- Participants ----------------

    public void AddParticipant(Guid actorUserId, Guid userId, ParticipantMode mode)
    {
        EnsureOpen();
        RequireOrganizer(actorUserId);

        Guard.NotEmpty(userId, nameof(userId));
        Guard.Ensure(!IsMember(userId), "User is already a member.");

        Participants.Add(new Participant(EventId, userId, mode));
        RecalculateEndDateFromSchedule();
    }

    public void RemoveParticipant(Guid actorUserId, Guid participantId)
    {
        EnsureOpen();
        RequireOrganizer(actorUserId);

        var p = Participants.FirstOrDefault(x => x.EventMemberId == participantId);
        Guard.Ensure(p is not null, "Participant not found.");

        Participants.Remove(p);

        // cleanup grants related to removed participant
        LocationGrants.RemoveAll(g =>
            g.GrantedByMemberId == participantId || g.GrantedToMemberId == participantId);
    }

    public void SetParticipantMode(Guid actorUserId, Guid participantId, ParticipantMode mode)
    {
        EnsureOpen();
        RequireOrganizer(actorUserId);

        var p = Participants.FirstOrDefault(x => x.EventMemberId == participantId);
        Guard.Ensure(p is not null, "Participant not found.");
        p.SetMode(mode);
    }

    // ---------------- Capability rules ----------------

    public void AllowCapabilityForMode(Guid actorUserId, ParticipantMode mode, Capability capability)
    {
        EnsureOpen();
        RequireOrganizer(actorUserId);

        if (mode == ParticipantMode.Active) return;
        PassiveAllowedCapabilities.Add(capability);
    }

    public void DenyCapabilityForMode(Guid actorUserId, ParticipantMode mode, Capability capability)
    {
        EnsureOpen();
        RequireOrganizer(actorUserId);

        if (mode == ParticipantMode.Active) return;
        PassiveAllowedCapabilities.Remove(capability);
    }

    public bool CanUser(Guid actorUserId, Capability capability)
    {
        var m = RequireActiveMember(actorUserId);

        if (m is Organizer) return true;

        var p = (Participant)m;
        if (p.Mode == ParticipantMode.Active) return true;

        return PassiveAllowedCapabilities.Contains(capability);
    }

    // ---------------- Location sharing (global) ----------------

    public void StartLocationSharing(Guid actorUserId)
    {
        EnsureOpen();
        RequireOrganizer(actorUserId);
        IsLocationSharingActive = true;
    }

    public void StopLocationSharing(Guid actorUserId)
    {
        EnsureOpen();
        RequireOrganizer(actorUserId);
        IsLocationSharingActive = false;
    }

    public void SetLocationShareScope(Guid actorUserId, LocationShareScope scope)
    {
        EnsureOpen();
        RequireOrganizer(actorUserId);
        LocationShareScope = scope;
    }

    // ---------------- Location sharing (participant-to-participant) ----------------

    // actor shares THEIR location with viewer
    public void GrantLocationAccess(Guid actorUserId, Guid viewerUserId)
    {
        EnsureOpen();

        Guard.Ensure(IsLocationSharingActive, "Location sharing is not active.");
        Guard.Ensure(LocationShareScope == LocationShareScope.Custom, "Custom sharing is disabled.");

        var actor = RequireActiveMember(actorUserId);
        var viewer = RequireActiveMember(viewerUserId);

        Guard.Ensure(actor is Participant, "Only participants can grant access.");
        Guard.Ensure(actor.EventMemberId != viewer.EventMemberId, "Cannot grant to self.");

        var existing = LocationGrants.FirstOrDefault(g =>
            g.GrantedByMemberId == actor.EventMemberId &&
            g.GrantedToMemberId == viewer.EventMemberId);

        if (existing is null)
            LocationGrants.Add(new LocationGrant(actor.EventMemberId, viewer.EventMemberId));
        else
            existing.Activate();
    }

    public void RevokeLocationAccess(Guid actorUserId, Guid viewerUserId)
    {
        EnsureOpen();

        var actor = RequireMember(actorUserId);
        var viewer = RequireMember(viewerUserId);

        var existing = LocationGrants.FirstOrDefault(g =>
            g.IsActive &&
            g.GrantedByMemberId == actor.EventMemberId &&
            g.GrantedToMemberId == viewer.EventMemberId);

        if (existing is null) return;
        existing.Deactivate();
    }

    public bool CanViewLocation(Guid viewerUserId, Guid targetUserId)
    {
        if (!IsLocationSharingActive) return false;

        var viewer = TryGetActiveMember(viewerUserId);
        var target = TryGetActiveMember(targetUserId);
        if (viewer is null || target is null) return false;

        // organizers see all
        if (viewer is Organizer) return true;

        // participants: only if custom + grant exists
        if (LocationShareScope != LocationShareScope.Custom) return false;

        // viewer can see target only if target granted viewer access
        return LocationGrants.Any(g =>
            g.IsActive &&
            g.GrantedByMemberId == target.EventMemberId &&
            g.GrantedToMemberId == viewer.EventMemberId);
    }

    // ---------------- Scheduling entrypoints ----------------

    public EventDay AddEventDay(Guid actorUserId, DateTime date, string title)
    {
        EnsureOpen();
        RequireOrganizer(actorUserId);

        var day = new EventDay(EventId, date, title, EventDays.Count);
        EventDays.Add(day);

        RecalculateEndDateFromSchedule();
        return day;
    }

    public void RemoveEventDay(Guid actorUserId, Guid eventDayId)
    {
        EnsureOpen();
        RequireOrganizer(actorUserId);

        var d = EventDays.FirstOrDefault(x => x.EventDayId == eventDayId);
        Guard.Ensure(d is not null, "Event day not found.");
        EventDays.Remove(d);

        RecalculateEndDateFromSchedule();
    }

    private void RecalculateEndDateFromSchedule()
    {
        if (EventDays.Count == 0)
        {
            EndDate = null;
            return;
        }

        var lastDay = EventDays.OrderBy(d => d.Date).Last();
        EndDate = lastDay.Date;
    }

    // ---------------- Chat (single room) ----------------

    public ChatMessage SendMessage(Guid actorUserId, string content)
    {
        EnsureOpen();
        Guard.Ensure(CanUser(actorUserId, Capability.ChatWrite), "User lacks ChatWrite capability.");

        var sender = RequireActiveMember(actorUserId);

        var msg = new ChatMessage(ChatRoom.ChatRoomId, sender.EventMemberId, content);
        ChatRoom.AddMessage(msg);
        return msg;
    }

    // ---------------- Voice (single channel) ----------------

    public void OpenVoice(Guid actorUserId)
    {
        EnsureOpen();
        RequireOrganizer(actorUserId);
        VoiceChannel.Open();
    }

    public void CloseVoice(Guid actorUserId)
    {
        EnsureOpen();
        RequireOrganizer(actorUserId);
        VoiceChannel.Close();
    }

    // ---------------- Media ----------------

    public EventMedia UploadMedia(Guid actorUserId, string mediaType, string fileUrl)
    {
        EnsureOpen();
        Guard.Ensure(CanUser(actorUserId, Capability.UploadMedia), "User lacks UploadMedia capability.");

        var sender = RequireActiveMember(actorUserId);

        var media = new EventMedia(EventId, sender.EventMemberId, mediaType, fileUrl);
        EventMedia.Add(media);
        return media;
    }

    public void RemoveMedia(Guid actorUserId, Guid mediaId)
    {
        EnsureOpen();
        RequireOrganizer(actorUserId);

        var m = EventMedia.FirstOrDefault(x => x.MediaId == mediaId);
        Guard.Ensure(m is not null, "Media not found.");
        EventMedia.Remove(m);
    }

    // ---------------- Guards ----------------

    private void EnsureOpen()
    {
        Guard.Ensure(Status != EventStatus.Cancelled && Status != EventStatus.Completed,
            "Event is closed and cannot be modified.");
    }

    private Organizer RequireOwner(Guid actorUserId)
    {
        var owner = Organizers.FirstOrDefault(o => o.EventMemberId == OwnerOrganizerId);
        Guard.Ensure(owner is not null, "Owner not found.");

        if (owner.UserId != actorUserId)
            throw new ForbiddenException("Owner permission required.");

        Guard.Ensure(owner.Status == MembershipStatus.Active, "Owner must be active.");
        return owner;
    }

    private Organizer RequireOrganizer(Guid actorUserId)
    {
        var org = Organizers.FirstOrDefault(o => o.EventMemberId == actorUserId);
        if (org is null)
            throw new ForbiddenException("Organizer permission required.");

        return org;
    }

    private EventMember RequireMember(Guid userId)
    {
        var m = Organizers.Cast<EventMember>().Concat(Participants).FirstOrDefault(x => x.UserId == userId);
        Guard.Ensure(m is not null, "User is not a member of this event.");
        return m!;
    }

    private EventMember RequireActiveMember(Guid userId)
    {
        var m = RequireMember(userId);
        Guard.Ensure(m.Status == MembershipStatus.Active, "User is not active in this event.");
        return m;
    }

    private EventMember? TryGetActiveMember(Guid userId)
    {
        var m = Organizers.Cast<EventMember>().Concat(Participants).FirstOrDefault(x => x.UserId == userId);
        if (m is null) return null;
        return m.Status == MembershipStatus.Active ? m : null;
    }

    private bool IsMember(Guid userId)
        => Organizers.Any(o => o.UserId == userId) || Participants.Any(p => p.UserId == userId);

    // ---------------- Join credentials internals ----------------
    // Stored format: "<saltB64>.<hashB64>" in JoinPasswordHash (single column)

    private static string HashJoinPassword(string password)
    {
        password = Guard.Required(password, "JoinPassword", 200);

        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            password: password,
            salt: salt,
            iterations: 100_000,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: 32);

        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    private static bool VerifyJoinPassword(string password, string stored)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(stored))
            return false;

        var parts = stored.Split('.', 2);
        if (parts.Length != 2) return false;

        var salt = Convert.FromBase64String(parts[0]);
        var storedHash = Convert.FromBase64String(parts[1]);

        var computed = Rfc2898DeriveBytes.Pbkdf2(
            password: password,
            salt: salt,
            iterations: 100_000,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: 32);

        return CryptographicOperations.FixedTimeEquals(computed, storedHash);
    }

    private static string GenerateJoinCode(int length)
    {
        Guard.Ensure(length >= 6 && length <= 12, "JoinCode length must be between 6 and 12.");

        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no I,O,1,0 to avoid confusion
        Span<byte> bytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(bytes);

        var chars = new char[length];
        for (int i = 0; i < length; i++)
        {
            chars[i] = alphabet[bytes[i] % alphabet.Length];
        }

        return new string(chars);
    }
}