using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urfair.Api.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class DeletionPeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EndedAt",
                table: "FairSlots",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndedAt",
                table: "FairSlots");
        }
    }
}
