using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaGetto.Database.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddMaliciousPackageBlocking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BlockMaliciousPackages",
                table: "Feeds",
                type: "bit",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MaliciousPackageAllowances",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Ecosystem = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaliciousPackageAllowances", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "MaliciousPackages",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Ecosystem = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    AdvisoryId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    AffectedVersions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ModifiedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaliciousPackages", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "MaliciousPackageSources",
                columns: table => new
                {
                    Ecosystem = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CursorUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RefreshedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AttemptedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaliciousPackageSources", x => x.Ecosystem);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MaliciousPackageAllowances_Ecosystem_Name",
                table: "MaliciousPackageAllowances",
                columns: new[] { "Ecosystem", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_MaliciousPackages_Ecosystem_AdvisoryId",
                table: "MaliciousPackages",
                columns: new[] { "Ecosystem", "AdvisoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_MaliciousPackages_Ecosystem_Name",
                table: "MaliciousPackages",
                columns: new[] { "Ecosystem", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MaliciousPackageAllowances");

            migrationBuilder.DropTable(
                name: "MaliciousPackages");

            migrationBuilder.DropTable(
                name: "MaliciousPackageSources");

            migrationBuilder.DropColumn(
                name: "BlockMaliciousPackages",
                table: "Feeds");
        }
    }
}
