using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaGetto.Database.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddArtifactFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ArtifactFiles",
                columns: table => new
                {
                    Key = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ArtifactVersionKey = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, collation: "utf8mb4_unicode_ci"),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Sha1 = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false, collation: "utf8mb4_unicode_ci"),
                    Md5 = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, collation: "utf8mb4_unicode_ci"),
                    Sha256 = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_unicode_ci"),
                    Sha512 = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_unicode_ci"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtifactFiles", x => x.Key);
                    table.ForeignKey(
                        name: "FK_ArtifactFiles_ArtifactVersions_ArtifactVersionKey",
                        column: x => x.ArtifactVersionKey,
                        principalTable: "ArtifactVersions",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "IX_ArtifactFiles_ArtifactVersionKey_FileName",
                table: "ArtifactFiles",
                columns: new[] { "ArtifactVersionKey", "FileName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArtifactFiles");
        }
    }
}
