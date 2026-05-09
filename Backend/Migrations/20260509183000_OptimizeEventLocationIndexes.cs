using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TripPlanner.Api.Data;

#nullable disable

namespace Backend.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260509183000_OptimizeEventLocationIndexes")]
    public partial class OptimizeEventLocationIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_EventMembers_UserId_Status",
                table: "EventMembers",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EventDays_EventId_Date_DayOrder",
                table: "EventDays",
                columns: new[] { "EventId", "Date", "DayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Activities_EventDayId_ActivityOrder",
                table: "Activities",
                columns: new[] { "EventDayId", "ActivityOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_LocationPoints_LocationSessionId_RecordedAt",
                table: "LocationPoints",
                columns: new[] { "LocationSessionId", "RecordedAt" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LocationPoints_LocationSessionId_RecordedAt",
                table: "LocationPoints");

            migrationBuilder.DropIndex(
                name: "IX_Activities_EventDayId_ActivityOrder",
                table: "Activities");

            migrationBuilder.DropIndex(
                name: "IX_EventDays_EventId_Date_DayOrder",
                table: "EventDays");

            migrationBuilder.DropIndex(
                name: "IX_EventMembers_UserId_Status",
                table: "EventMembers");
        }
    }
}
