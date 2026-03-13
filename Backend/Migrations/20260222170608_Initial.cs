using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "FK_RefreshTokens_Users_UserId1",
                table: "RefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_UserId1",
                table: "RefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_EventMembers_EventId1",
                table: "EventMembers");

            migrationBuilder.DropIndex(
                name: "IX_EventMembers_EventId2",
                table: "EventMembers");

            migrationBuilder.DropIndex(
                name: "IX_EventMedia_EventId1",
                table: "EventMedia");

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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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

            migrationBuilder.AddColumn<Guid>(
                name: "EventId1",
                table: "EventMedia",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId1",
                table: "RefreshTokens",
                column: "UserId1");

            migrationBuilder.CreateIndex(
                name: "IX_EventMembers_EventId1",
                table: "EventMembers",
                column: "EventId1");

            migrationBuilder.CreateIndex(
                name: "IX_EventMembers_EventId2",
                table: "EventMembers",
                column: "EventId2");

            migrationBuilder.CreateIndex(
                name: "IX_EventMedia_EventId1",
                table: "EventMedia",
                column: "EventId1");

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
                name: "FK_RefreshTokens_Users_UserId1",
                table: "RefreshTokens",
                column: "UserId1",
                principalTable: "Users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
