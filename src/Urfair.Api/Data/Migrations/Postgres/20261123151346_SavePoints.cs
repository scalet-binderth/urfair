using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urfair.Api.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class SavePoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AllocationSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FairSlotId = table.Column<Guid>(type: "uuid", nullable: false),
                    LogId = table.Column<Guid>(type: "uuid", nullable: false),
                    Data = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllocationSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AllocationSnapshots_FairSlots_FairSlotId",
                        column: x => x.FairSlotId,
                        principalTable: "FairSlots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AllocationSnapshots_FairSlotId_LogId",
                table: "AllocationSnapshots",
                columns: new[] { "FairSlotId", "LogId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AllocationSnapshots");
        }
    }
}
