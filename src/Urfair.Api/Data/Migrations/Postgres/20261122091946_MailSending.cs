using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urfair.Api.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class MailSending : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Attempts",
                table: "Mails",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Error",
                table: "Mails",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAt",
                table: "Mails",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Outbox",
                table: "Mails",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ReplyTo",
                table: "Mails",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SentAt",
                table: "Mails",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrganiserEmail",
                table: "FairSlots",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Mails_Outbox_SentAt",
                table: "Mails",
                columns: new[] { "Outbox", "SentAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Mails_Outbox_SentAt",
                table: "Mails");

            migrationBuilder.DropColumn(
                name: "Attempts",
                table: "Mails");

            migrationBuilder.DropColumn(
                name: "Error",
                table: "Mails");

            migrationBuilder.DropColumn(
                name: "NextAttemptAt",
                table: "Mails");

            migrationBuilder.DropColumn(
                name: "Outbox",
                table: "Mails");

            migrationBuilder.DropColumn(
                name: "ReplyTo",
                table: "Mails");

            migrationBuilder.DropColumn(
                name: "SentAt",
                table: "Mails");

            migrationBuilder.DropColumn(
                name: "OrganiserEmail",
                table: "FairSlots");
        }
    }
}
