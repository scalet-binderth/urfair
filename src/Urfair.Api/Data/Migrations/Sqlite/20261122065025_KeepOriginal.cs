using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urfair.Api.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class KeepOriginal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AllocationLog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FairSlotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    At = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Actor = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Person = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    Before = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    After = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllocationLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AllocationLog_FairSlots_FairSlotId",
                        column: x => x.FairSlotId,
                        principalTable: "FairSlots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OriginalAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FairSlotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    BallotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OriginalAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OriginalAssignments_Ballots_BallotId",
                        column: x => x.BallotId,
                        principalTable: "Ballots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OriginalAssignments_FairSlots_FairSlotId",
                        column: x => x.FairSlotId,
                        principalTable: "FairSlots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OriginalAssignments_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AllocationLog_FairSlotId",
                table: "AllocationLog",
                column: "FairSlotId");

            migrationBuilder.CreateIndex(
                name: "IX_OriginalAssignments_BallotId",
                table: "OriginalAssignments",
                column: "BallotId");

            migrationBuilder.CreateIndex(
                name: "IX_OriginalAssignments_FairSlotId",
                table: "OriginalAssignments",
                column: "FairSlotId");

            migrationBuilder.CreateIndex(
                name: "IX_OriginalAssignments_SessionId",
                table: "OriginalAssignments",
                column: "SessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AllocationLog");

            migrationBuilder.DropTable(
                name: "OriginalAssignments");
        }
    }
}
