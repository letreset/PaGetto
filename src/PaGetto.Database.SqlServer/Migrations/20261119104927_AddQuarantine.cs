using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaGetto.Database.SqlServer.Migrations
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
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QuarantineReleasedAtUtc",
                table: "Packages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QuarantinedAtUtc",
                table: "Packages",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "QuarantineMaliciousPackages",
                table: "Feeds",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "QuarantineSeverity",
                table: "Feeds",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuarantineReason",
                table: "ArtifactVersions",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QuarantineReleasedAtUtc",
                table: "ArtifactVersions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QuarantinedAtUtc",
                table: "ArtifactVersions",
                type: "datetime2",
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
