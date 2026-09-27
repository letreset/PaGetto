using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaGetto.Database.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedMirrors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeedMirrors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FeedId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    PackageSource = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Legacy = table.Column<bool>(type: "bit", nullable: false),
                    DownloadTimeoutSeconds = table.Column<int>(type: "int", nullable: true),
                    AuthType = table.Column<int>(type: "int", nullable: true),
                    AuthUsername = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AuthPassword = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AuthToken = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AuthCustomHeaders = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedMirrors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeedMirrors_Feeds_FeedId",
                        column: x => x.FeedId,
                        principalTable: "Feeds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeedMirrors_FeedId_SortOrder",
                table: "FeedMirrors",
                columns: new[] { "FeedId", "SortOrder" });

            // Move each feed's mirror into FeedMirrors before the old columns are dropped. A feed
            // without a package source never mirrored anything, so it gets no row.
            migrationBuilder.Sql(
                @"INSERT INTO FeedMirrors (FeedId, SortOrder, Enabled, PackageSource, Legacy, DownloadTimeoutSeconds, AuthType, AuthUsername, AuthPassword, AuthToken, AuthCustomHeaders)
                  SELECT Id, 0, MirrorEnabled, MirrorPackageSource, MirrorLegacy, MirrorDownloadTimeoutSeconds, MirrorAuthType, MirrorAuthUsername, MirrorAuthPassword, MirrorAuthToken, MirrorAuthCustomHeaders
                  FROM Feeds
                  WHERE MirrorPackageSource IS NOT NULL AND MirrorPackageSource <> '';");

            migrationBuilder.DropColumn(
                name: "MirrorAuthCustomHeaders",
                table: "Feeds");

            migrationBuilder.DropColumn(
                name: "MirrorAuthPassword",
                table: "Feeds");

            migrationBuilder.DropColumn(
                name: "MirrorAuthToken",
                table: "Feeds");

            migrationBuilder.DropColumn(
                name: "MirrorAuthType",
                table: "Feeds");

            migrationBuilder.DropColumn(
                name: "MirrorAuthUsername",
                table: "Feeds");

            migrationBuilder.DropColumn(
                name: "MirrorDownloadTimeoutSeconds",
                table: "Feeds");

            migrationBuilder.DropColumn(
                name: "MirrorEnabled",
                table: "Feeds");

            migrationBuilder.DropColumn(
                name: "MirrorLegacy",
                table: "Feeds");

            migrationBuilder.DropColumn(
                name: "MirrorPackageSource",
                table: "Feeds");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MirrorAuthCustomHeaders",
                table: "Feeds",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MirrorAuthPassword",
                table: "Feeds",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MirrorAuthToken",
                table: "Feeds",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MirrorAuthType",
                table: "Feeds",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MirrorAuthUsername",
                table: "Feeds",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MirrorDownloadTimeoutSeconds",
                table: "Feeds",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MirrorEnabled",
                table: "Feeds",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "MirrorLegacy",
                table: "Feeds",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MirrorPackageSource",
                table: "Feeds",
                type: "nvarchar(max)",
                nullable: true);

            // Copy each feed's first mirror back into the Feeds columns before FeedMirrors is dropped.
            migrationBuilder.Sql(
                @"UPDATE f
                  SET MirrorEnabled = m.Enabled, MirrorPackageSource = m.PackageSource, MirrorLegacy = m.Legacy,
                      MirrorDownloadTimeoutSeconds = m.DownloadTimeoutSeconds, MirrorAuthType = m.AuthType,
                      MirrorAuthUsername = m.AuthUsername, MirrorAuthPassword = m.AuthPassword,
                      MirrorAuthToken = m.AuthToken, MirrorAuthCustomHeaders = m.AuthCustomHeaders
                  FROM Feeds f
                  JOIN (SELECT *, ROW_NUMBER() OVER (PARTITION BY FeedId ORDER BY SortOrder, Id) AS rn FROM FeedMirrors) m
                    ON m.FeedId = f.Id AND m.rn = 1;");

            migrationBuilder.DropTable(
                name: "FeedMirrors");
        }
    }
}
