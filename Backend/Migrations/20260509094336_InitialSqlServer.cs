using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class InitialSqlServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: false),
                    ProfilePictureUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    AccountStatus = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EmailVerificationCodeHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EmailVerificationExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EmailVerified = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    PasswordSalt = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    RefreshTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReplacedByTokenId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.RefreshTokenId);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Activities",
                columns: table => new
                {
                    ActivityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventDayId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    StartTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    ThumbnailUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    LocationName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Latitude = table.Column<double>(type: "float", nullable: false),
                    Longitude = table.Column<double>(type: "float", nullable: false),
                    ActivityOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Activities", x => x.ActivityId);
                    table.CheckConstraint("CK_Activities_TimeRange", "[StartTime] IS NULL OR [EndTime] IS NULL OR [EndTime] >= [StartTime]");
                });

            migrationBuilder.CreateTable(
                name: "ActivitySteps",
                columns: table => new
                {
                    ActivityStepId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActivityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StepOrder = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    IsMandatory = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivitySteps", x => x.ActivityStepId);
                    table.ForeignKey(
                        name: "FK_ActivitySteps_Activities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "Activities",
                        principalColumn: "ActivityId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    ChatMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChatRoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SentByEventMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.ChatMessageId);
                });

            migrationBuilder.CreateTable(
                name: "ChatRooms",
                columns: table => new
                {
                    ChatRoomId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatRooms", x => x.ChatRoomId);
                });

            migrationBuilder.CreateTable(
                name: "EventDays",
                columns: table => new
                {
                    EventDayId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    DayOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventDays", x => x.EventDayId);
                });

            migrationBuilder.CreateTable(
                name: "EventLocationGrants",
                columns: table => new
                {
                    LocationGrantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrantedByMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrantedToMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventLocationGrants", x => x.LocationGrantId);
                });

            migrationBuilder.CreateTable(
                name: "EventMedia",
                columns: table => new
                {
                    MediaId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UploadedByEventMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MediaType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    FileUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventMedia", x => x.MediaId);
                });

            migrationBuilder.CreateTable(
                name: "EventMembers",
                columns: table => new
                {
                    EventMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    MemberType = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    DisplayTitle = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    Mode = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventMembers", x => x.EventMemberId);
                    table.ForeignKey(
                        name: "FK_EventMembers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    EventType = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DestinationName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DestinationLatitude = table.Column<double>(type: "float", nullable: false),
                    DestinationLongitude = table.Column<double>(type: "float", nullable: false),
                    ThumbnailUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OwnerOrganizerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsLocationSharingActive = table.Column<bool>(type: "bit", nullable: false),
                    LocationShareScope = table.Column<int>(type: "int", nullable: false),
                    PassiveAllowedCapabilities = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    JoinCode = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    JoinPasswordEncrypted = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    IsJoinEnabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.EventId);
                    table.CheckConstraint("CK_Events_DateRange", "[EndDate] IS NULL OR [EndDate] >= [StartDate]");
                    table.ForeignKey(
                        name: "FK_Events_EventMembers_OwnerOrganizerId",
                        column: x => x.OwnerOrganizerId,
                        principalTable: "EventMembers",
                        principalColumn: "EventMemberId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LocationSessions",
                columns: table => new
                {
                    LocationSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationSessions", x => x.LocationSessionId);
                    table.ForeignKey(
                        name: "FK_LocationSessions_EventMembers_EventMemberId",
                        column: x => x.EventMemberId,
                        principalTable: "EventMembers",
                        principalColumn: "EventMemberId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VoiceChannels",
                columns: table => new
                {
                    VoiceChannelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    IsOpen = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoiceChannels", x => x.VoiceChannelId);
                    table.ForeignKey(
                        name: "FK_VoiceChannels_Events_EventId",
                        column: x => x.EventId,
                        principalTable: "Events",
                        principalColumn: "EventId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LocationPoints",
                columns: table => new
                {
                    LocationPointId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Latitude = table.Column<double>(type: "float", nullable: false),
                    Longitude = table.Column<double>(type: "float", nullable: false),
                    Accuracy = table.Column<double>(type: "float", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationPoints", x => x.LocationPointId);
                    table.ForeignKey(
                        name: "FK_LocationPoints_LocationSessions_LocationSessionId",
                        column: x => x.LocationSessionId,
                        principalTable: "LocationSessions",
                        principalColumn: "LocationSessionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VoiceSessions",
                columns: table => new
                {
                    VoiceSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoiceChannelId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedByEventMemberId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsRecorded = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoiceSessions", x => x.VoiceSessionId);
                    table.ForeignKey(
                        name: "FK_VoiceSessions_EventMembers_StartedByEventMemberId",
                        column: x => x.StartedByEventMemberId,
                        principalTable: "EventMembers",
                        principalColumn: "EventMemberId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VoiceSessions_VoiceChannels_VoiceChannelId",
                        column: x => x.VoiceChannelId,
                        principalTable: "VoiceChannels",
                        principalColumn: "VoiceChannelId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VoiceRecordings",
                columns: table => new
                {
                    VoiceRecordingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VoiceSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileUrl = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    DurationSeconds = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VoiceRecordings", x => x.VoiceRecordingId);
                    table.ForeignKey(
                        name: "FK_VoiceRecordings_VoiceSessions_VoiceSessionId",
                        column: x => x.VoiceSessionId,
                        principalTable: "VoiceSessions",
                        principalColumn: "VoiceSessionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Activities_EventDayId",
                table: "Activities",
                column: "EventDayId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivitySteps_ActivityId",
                table: "ActivitySteps",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ChatRoomId",
                table: "ChatMessages",
                column: "ChatRoomId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_SentByEventMemberId",
                table: "ChatMessages",
                column: "SentByEventMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatRooms_EventId",
                table: "ChatRooms",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventDays_EventId",
                table: "EventDays",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_EventLocationGrants_EventId_GrantedByMemberId_GrantedToMemberId",
                table: "EventLocationGrants",
                columns: new[] { "EventId", "GrantedByMemberId", "GrantedToMemberId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventLocationGrants_GrantedByMemberId",
                table: "EventLocationGrants",
                column: "GrantedByMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_EventLocationGrants_GrantedToMemberId",
                table: "EventLocationGrants",
                column: "GrantedToMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_EventMedia_EventId",
                table: "EventMedia",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_EventMedia_UploadedByEventMemberId",
                table: "EventMedia",
                column: "UploadedByEventMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_EventMembers_EventId_UserId",
                table: "EventMembers",
                columns: new[] { "EventId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventMembers_UserId",
                table: "EventMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Events_JoinCode",
                table: "Events",
                column: "JoinCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_OwnerOrganizerId",
                table: "Events",
                column: "OwnerOrganizerId");

            migrationBuilder.CreateIndex(
                name: "IX_LocationPoints_LocationSessionId",
                table: "LocationPoints",
                column: "LocationSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_LocationSessions_EventMemberId",
                table: "LocationSessions",
                column: "EventMemberId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TokenHash",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId",
                table: "RefreshTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VoiceChannels_EventId",
                table: "VoiceChannels",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VoiceRecordings_VoiceSessionId",
                table: "VoiceRecordings",
                column: "VoiceSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_VoiceSessions_StartedByEventMemberId",
                table: "VoiceSessions",
                column: "StartedByEventMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_VoiceSessions_VoiceChannelId",
                table: "VoiceSessions",
                column: "VoiceChannelId");

            migrationBuilder.AddForeignKey(
                name: "FK_Activities_EventDays_EventDayId",
                table: "Activities",
                column: "EventDayId",
                principalTable: "EventDays",
                principalColumn: "EventDayId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ChatMessages_ChatRooms_ChatRoomId",
                table: "ChatMessages",
                column: "ChatRoomId",
                principalTable: "ChatRooms",
                principalColumn: "ChatRoomId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ChatMessages_EventMembers_SentByEventMemberId",
                table: "ChatMessages",
                column: "SentByEventMemberId",
                principalTable: "EventMembers",
                principalColumn: "EventMemberId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ChatRooms_Events_EventId",
                table: "ChatRooms",
                column: "EventId",
                principalTable: "Events",
                principalColumn: "EventId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_EventDays_Events_EventId",
                table: "EventDays",
                column: "EventId",
                principalTable: "Events",
                principalColumn: "EventId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_EventLocationGrants_EventMembers_GrantedByMemberId",
                table: "EventLocationGrants",
                column: "GrantedByMemberId",
                principalTable: "EventMembers",
                principalColumn: "EventMemberId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EventLocationGrants_EventMembers_GrantedToMemberId",
                table: "EventLocationGrants",
                column: "GrantedToMemberId",
                principalTable: "EventMembers",
                principalColumn: "EventMemberId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EventLocationGrants_Events_EventId",
                table: "EventLocationGrants",
                column: "EventId",
                principalTable: "Events",
                principalColumn: "EventId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_EventMedia_EventMembers_UploadedByEventMemberId",
                table: "EventMedia",
                column: "UploadedByEventMemberId",
                principalTable: "EventMembers",
                principalColumn: "EventMemberId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EventMedia_Events_EventId",
                table: "EventMedia",
                column: "EventId",
                principalTable: "Events",
                principalColumn: "EventId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_EventMembers_Events_EventId",
                table: "EventMembers",
                column: "EventId",
                principalTable: "Events",
                principalColumn: "EventId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Events_EventMembers_OwnerOrganizerId",
                table: "Events");

            migrationBuilder.DropTable(
                name: "ActivitySteps");

            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropTable(
                name: "EventLocationGrants");

            migrationBuilder.DropTable(
                name: "EventMedia");

            migrationBuilder.DropTable(
                name: "LocationPoints");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "VoiceRecordings");

            migrationBuilder.DropTable(
                name: "Activities");

            migrationBuilder.DropTable(
                name: "ChatRooms");

            migrationBuilder.DropTable(
                name: "LocationSessions");

            migrationBuilder.DropTable(
                name: "VoiceSessions");

            migrationBuilder.DropTable(
                name: "EventDays");

            migrationBuilder.DropTable(
                name: "VoiceChannels");

            migrationBuilder.DropTable(
                name: "EventMembers");

            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
