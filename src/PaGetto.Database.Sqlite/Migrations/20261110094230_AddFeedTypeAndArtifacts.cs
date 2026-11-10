using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaGetto.Database.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedTypeAndArtifacts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "Feeds",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "Artifacts",
                columns: table => new
                {
                    Key = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FeedId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Artifacts", x => x.Key);
                    table.ForeignKey(
                        name: "FK_Artifacts_Feeds_FeedId",
                        column: x => x.FeedId,
                        principalTable: "Feeds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ArtifactVersions",
                columns: table => new
                {
                    Key = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ArtifactKey = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    NormalizedVersion = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    IsPrerelease = table.Column<bool>(type: "INTEGER", nullable: false),
                    Listed = table.Column<bool>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    MetadataJson = table.Column<string>(type: "TEXT", nullable: true),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Downloads = table.Column<long>(type: "INTEGER", nullable: false),
                    HasReadme = table.Column<bool>(type: "INTEGER", nullable: false),
                    CachedFrom = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Published = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtifactVersions", x => x.Key);
                    table.ForeignKey(
                        name: "FK_ArtifactVersions_Artifacts_ArtifactKey",
                        column: x => x.ArtifactKey,
                        principalTable: "Artifacts",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Artifacts_FeedId_Name",
                table: "Artifacts",
                columns: new[] { "FeedId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArtifactVersions_ArtifactKey_NormalizedVersion",
                table: "ArtifactVersions",
                columns: new[] { "ArtifactKey", "NormalizedVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArtifactVersions");

            migrationBuilder.DropTable(
                name: "Artifacts");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Feeds");
        }
    }
}
