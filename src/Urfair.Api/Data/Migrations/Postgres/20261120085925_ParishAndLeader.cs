using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urfair.Api.Data.Migrations.Postgres
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
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Parish",
                table: "FairSlots",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Leader",
                table: "Activities",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Parish",
                table: "Activities",
                type: "character varying(120)",
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
