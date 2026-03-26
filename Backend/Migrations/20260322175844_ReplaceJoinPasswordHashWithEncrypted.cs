using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceJoinPasswordHashWithEncrypted : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "JoinPasswordHash",
                table: "Events");

            migrationBuilder.AddColumn<string>(
                name: "JoinPasswordEncrypted",
                table: "Events",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "JoinPasswordEncrypted",
                table: "Events");

            migrationBuilder.AddColumn<string>(
                name: "JoinPasswordHash",
                table: "Events",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");
        }
    }
}
