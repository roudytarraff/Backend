using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityDriverAssignment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DriverDisplayName",
                table: "Activities",
                type: "nvarchar(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DriverParticipantId",
                table: "Activities",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Activities_DriverParticipantId",
                table: "Activities",
                column: "DriverParticipantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Activities_DriverParticipantId",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "DriverDisplayName",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "DriverParticipantId",
                table: "Activities");
        }
    }
}
