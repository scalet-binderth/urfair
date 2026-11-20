using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urfair.Api.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class ParishAndLeader : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Organiser",
                table: "FairSlots",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Parish",
                table: "FairSlots",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Leader",
                table: "Activities",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Parish",
                table: "Activities",
                type: "TEXT",
                maxLength: 120,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Organiser",
                table: "FairSlots");

            migrationBuilder.DropColumn(
                name: "Parish",
                table: "FairSlots");

            migrationBuilder.DropColumn(
                name: "Leader",
                table: "Activities");

            migrationBuilder.DropColumn(
                name: "Parish",
                table: "Activities");
        }
    }
}
