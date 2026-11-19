using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaGetto.Database.MySql.Migrations
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
                type: "tinyint(1)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MaliciousPackageAllowances",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Ecosystem = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_unicode_ci"),
                    Name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, collation: "utf8mb4_unicode_ci"),
                    Version = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true, collation: "utf8mb4_unicode_ci"),
                    Reason = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true, collation: "utf8mb4_unicode_ci"),
                    CreatedBy = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true, collation: "utf8mb4_unicode_ci"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaliciousPackageAllowances", x => x.Key);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "MaliciousPackages",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Ecosystem = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_unicode_ci"),
                    Name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, collation: "utf8mb4_unicode_ci"),
                    AdvisoryId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_unicode_ci"),
                    Summary = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true, collation: "utf8mb4_unicode_ci"),
                    AffectedVersions = table.Column<string>(type: "longtext", nullable: true, collation: "utf8mb4_unicode_ci"),
                    ModifiedUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaliciousPackages", x => x.Key);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "MaliciousPackageSources",
                columns: table => new
                {
                    Ecosystem = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_unicode_ci"),
                    CursorUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    RefreshedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    AttemptedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LastError = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true, collation: "utf8mb4_unicode_ci")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaliciousPackageSources", x => x.Ecosystem);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

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
