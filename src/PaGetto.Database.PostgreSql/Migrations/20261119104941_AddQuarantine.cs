using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaGetto.Database.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddQuarantine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "QuarantineReason",
                table: "Packages",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QuarantineReleasedAtUtc",
                table: "Packages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QuarantinedAtUtc",
                table: "Packages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "QuarantineMaliciousPackages",
                table: "Feeds",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "QuarantineSeverity",
                table: "Feeds",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuarantineReason",
                table: "ArtifactVersions",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QuarantineReleasedAtUtc",
                table: "ArtifactVersions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QuarantinedAtUtc",
                table: "ArtifactVersions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QuarantineReason",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "QuarantineReleasedAtUtc",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "QuarantinedAtUtc",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "QuarantineMaliciousPackages",
                table: "Feeds");

            migrationBuilder.DropColumn(
                name: "QuarantineSeverity",
                table: "Feeds");

            migrationBuilder.DropColumn(
                name: "QuarantineReason",
                table: "ArtifactVersions");

            migrationBuilder.DropColumn(
                name: "QuarantineReleasedAtUtc",
                table: "ArtifactVersions");

            migrationBuilder.DropColumn(
                name: "QuarantinedAtUtc",
                table: "ArtifactVersions");
        }
    }
}
