using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BillingPlatform",
                table: "Users",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingProductId",
                table: "Users",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingTransactionId",
                table: "Users",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BillingUpdatedAtUtc",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlusExpiresAtUtc",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SubscriptionPlan",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "PublishedAtUtc",
                table: "Events",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BillingPlatform",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "BillingProductId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "BillingTransactionId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "BillingUpdatedAtUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PlusExpiresAtUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SubscriptionPlan",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PublishedAtUtc",
                table: "Events");
        }
    }
}
