using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaGetto.Database.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddMirrorCacheRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastDownloadedAtUtc",
                table: "Packages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MirrorCacheRetentionDays",
                table: "Feeds",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastDownloadedAtUtc",
                table: "ArtifactVersions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastDownloadedAtUtc",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "MirrorCacheRetentionDays",
                table: "Feeds");

            migrationBuilder.DropColumn(
                name: "LastDownloadedAtUtc",
                table: "ArtifactVersions");
        }
    }
}
