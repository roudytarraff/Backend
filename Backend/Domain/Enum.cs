namespace TripPlanner.Api.Domain;

public enum AccountStatus { Active, Suspended, Deactivated }
public enum SubscriptionPlan { Free, Plus }
public enum EventStatus { Draft, Active, Cancelled, Completed }
public enum MembershipStatus { Active, Suspended, Left, Removed }
public enum ParticipantMode { Active, Passive }
public enum LocationShareScope { OrganizersOnly, Custom }
public enum LocationGrantStatus { Pending, Active }
public enum RefreshTokenStatus { Active, Revoked, Expired }

public enum Capability
{
    ViewSchedule,
    ChatRead,
    ChatWrite,
    VoiceJoin,
    ShareLocation,
    ViewLocations,
    UploadMedia
}

// ✅ NEW
public enum ActivityStatus
{
    NotStarted,
    Ongoing,
    Ended
}
