using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urfair.Api.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class RequiredStaffing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxExtra",
                table: "FairSlots",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "RequiredStaffing",
                table: "FairSlots",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Extra",
                table: "Ballots",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxExtra",
                table: "FairSlots");

            migrationBuilder.DropColumn(
                name: "RequiredStaffing",
                table: "FairSlots");

            migrationBuilder.DropColumn(
                name: "Extra",
                table: "Ballots");
        }
    }
}
