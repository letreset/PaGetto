using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaGetto.Database.Sqlite.Migrations
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
                    Key = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ArtifactVersionKey = table.Column<int>(type: "INTEGER", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha1 = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Md5 = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Sha512 = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
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
                });

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
