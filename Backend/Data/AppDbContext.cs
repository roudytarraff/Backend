using Microsoft.EntityFrameworkCore;
using TripPlanner.Api.Domain.Chat;
using TripPlanner.Api.Domain.Events;
using TripPlanner.Api.Domain.Location;
using TripPlanner.Api.Domain.Media;
using TripPlanner.Api.Domain.Schedule;
using TripPlanner.Api.Domain.Users;
using TripPlanner.Api.Domain.Voice;

namespace TripPlanner.Api.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventMember> EventMembers => Set<EventMember>();
    public DbSet<Organizer> Organizers => Set<Organizer>();
    public DbSet<Participant> Participants => Set<Participant>();

    public DbSet<EventDay> EventDays => Set<EventDay>();
    public DbSet<Activity> Activities => Set<Activity>();
    public DbSet<ActivityStep> ActivitySteps => Set<ActivityStep>();

    public DbSet<ChatRoom> ChatRooms => Set<ChatRoom>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    public DbSet<VoiceChannel> VoiceChannels => Set<VoiceChannel>();
    public DbSet<VoiceSession> VoiceSessions => Set<VoiceSession>();
    public DbSet<VoiceRecording> VoiceRecordings => Set<VoiceRecording>();

    public DbSet<LocationSession> LocationSessions => Set<LocationSession>();
    public DbSet<LocationPoint> LocationPoints => Set<LocationPoint>();

    public DbSet<EventMedia> EventMedia => Set<EventMedia>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ModelMap.Apply(modelBuilder);
    }
}
