using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class AdjustmentModelMap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "UserId1",
                table: "RefreshTokens",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "EventId1",
                table: "EventMembers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EventId2",
                table: "EventMembers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "MediaId",
                table: "EventMedia",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()",
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "EventId1",
                table: "EventMedia",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VoiceSessions_StartedByEventMemberId",
                table: "VoiceSessions",
                column: "StartedByEventMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId1",
                table: "RefreshTokens",
                column: "UserId1");

            migrationBuilder.CreateIndex(
                name: "IX_Events_OwnerOrganizerId",
                table: "Events",
                column: "OwnerOrganizerId");

            migrationBuilder.CreateIndex(
                name: "IX_EventMembers_EventId1",
                table: "EventMembers",
                column: "EventId1");

            migrationBuilder.CreateIndex(
                name: "IX_EventMembers_EventId2",
                table: "EventMembers",
                column: "EventId2");

            migrationBuilder.CreateIndex(
                name: "IX_EventMembers_UserId",
                table: "EventMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_EventMedia_EventId1",
                table: "EventMedia",
                column: "EventId1");

            migrationBuilder.CreateIndex(
                name: "IX_EventMedia_UploadedByEventMemberId",
                table: "EventMedia",
                column: "UploadedByEventMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_EventLocationGrants_GrantedByMemberId",
                table: "EventLocationGrants",
                column: "GrantedByMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_EventLocationGrants_GrantedToMemberId",
                table: "EventLocationGrants",
                column: "GrantedToMemberId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_SentByEventMemberId",
                table: "ChatMessages",
                column: "SentByEventMemberId");

            migrationBuilder.AddForeignKey(
                name: "FK_ChatMessages_EventMembers_SentByEventMemberId",
                table: "ChatMessages",
                column: "SentByEventMemberId",
                principalTable: "EventMembers",
                principalColumn: "EventMemberId",
                onDelete: ReferentialAction.Restrict);

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
                name: "FK_EventMedia_EventMembers_UploadedByEventMemberId",
                table: "EventMedia",
                column: "UploadedByEventMemberId",
                principalTable: "EventMembers",
                principalColumn: "EventMemberId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_EventMedia_Events_EventId1",
                table: "EventMedia",
                column: "EventId1",
                principalTable: "Events",
                principalColumn: "EventId");

            migrationBuilder.AddForeignKey(
                name: "FK_EventMembers_Events_EventId1",
                table: "EventMembers",
                column: "EventId1",
                principalTable: "Events",
                principalColumn: "EventId");

            migrationBuilder.AddForeignKey(
                name: "FK_EventMembers_Events_EventId2",
                table: "EventMembers",
                column: "EventId2",
                principalTable: "Events",
                principalColumn: "EventId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_EventMembers_Users_UserId",
                table: "EventMembers",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Events_EventMembers_OwnerOrganizerId",
                table: "Events",
                column: "OwnerOrganizerId",
                principalTable: "EventMembers",
                principalColumn: "EventMemberId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RefreshTokens_Users_UserId1",
                table: "RefreshTokens",
                column: "UserId1",
                principalTable: "Users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_VoiceSessions_EventMembers_StartedByEventMemberId",
                table: "VoiceSessions",
                column: "StartedByEventMemberId",
                principalTable: "EventMembers",
                principalColumn: "EventMemberId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatMessages_EventMembers_SentByEventMemberId",
                table: "ChatMessages");

            migrationBuilder.DropForeignKey(
                name: "FK_EventLocationGrants_EventMembers_GrantedByMemberId",
                table: "EventLocationGrants");

            migrationBuilder.DropForeignKey(
                name: "FK_EventLocationGrants_EventMembers_GrantedToMemberId",
                table: "EventLocationGrants");

            migrationBuilder.DropForeignKey(
                name: "FK_EventMedia_EventMembers_UploadedByEventMemberId",
                table: "EventMedia");

            migrationBuilder.DropForeignKey(
                name: "FK_EventMedia_Events_EventId1",
                table: "EventMedia");

            migrationBuilder.DropForeignKey(
                name: "FK_EventMembers_Events_EventId1",
                table: "EventMembers");

            migrationBuilder.DropForeignKey(
                name: "FK_EventMembers_Events_EventId2",
                table: "EventMembers");

            migrationBuilder.DropForeignKey(
                name: "FK_EventMembers_Users_UserId",
                table: "EventMembers");

            migrationBuilder.DropForeignKey(
                name: "FK_Events_EventMembers_OwnerOrganizerId",
                table: "Events");

            migrationBuilder.DropForeignKey(
                name: "FK_RefreshTokens_Users_UserId1",
                table: "RefreshTokens");

            migrationBuilder.DropForeignKey(
                name: "FK_VoiceSessions_EventMembers_StartedByEventMemberId",
                table: "VoiceSessions");

            migrationBuilder.DropIndex(
                name: "IX_VoiceSessions_StartedByEventMemberId",
                table: "VoiceSessions");

            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_UserId1",
                table: "RefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_Events_OwnerOrganizerId",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_EventMembers_EventId1",
                table: "EventMembers");

            migrationBuilder.DropIndex(
                name: "IX_EventMembers_EventId2",
                table: "EventMembers");

            migrationBuilder.DropIndex(
                name: "IX_EventMembers_UserId",
                table: "EventMembers");

            migrationBuilder.DropIndex(
                name: "IX_EventMedia_EventId1",
                table: "EventMedia");

            migrationBuilder.DropIndex(
                name: "IX_EventMedia_UploadedByEventMemberId",
                table: "EventMedia");

            migrationBuilder.DropIndex(
                name: "IX_EventLocationGrants_GrantedByMemberId",
                table: "EventLocationGrants");

            migrationBuilder.DropIndex(
                name: "IX_EventLocationGrants_GrantedToMemberId",
                table: "EventLocationGrants");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessages_SentByEventMemberId",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "UserId1",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "EventId1",
                table: "EventMembers");

            migrationBuilder.DropColumn(
                name: "EventId2",
                table: "EventMembers");

            migrationBuilder.DropColumn(
                name: "EventId1",
                table: "EventMedia");

            migrationBuilder.AlterColumn<Guid>(
                name: "MediaId",
                table: "EventMedia",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldDefaultValueSql: "gen_random_uuid()");
        }
    }
}
