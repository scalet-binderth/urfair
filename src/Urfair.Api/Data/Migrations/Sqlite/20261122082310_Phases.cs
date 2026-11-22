using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urfair.Api.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class Phases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShowInterim",
                table: "FairSlots",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // The former "Closed" was the internal allocation; "Closed" now means final lists.
            migrationBuilder.Sql("UPDATE \"FairSlots\" SET \"Phase\" = 'Allocation' WHERE \"Phase\" = 'Closed';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"FairSlots\" SET \"Phase\" = 'Closed' WHERE \"Phase\" = 'Allocation';");
            migrationBuilder.Sql("UPDATE \"FairSlots\" SET \"Phase\" = 'Published' WHERE \"Phase\" = 'Ended';");
            migrationBuilder.DropColumn(
                name: "ShowInterim",
                table: "FairSlots");
        }
    }
}
