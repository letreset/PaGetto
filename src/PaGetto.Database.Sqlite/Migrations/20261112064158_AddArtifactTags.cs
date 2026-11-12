using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaGetto.Database.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddArtifactTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ArtifactTags",
                columns: table => new
                {
                    Key = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ArtifactKey = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArtifactTags", x => x.Key);
                    table.ForeignKey(
                        name: "FK_ArtifactTags_Artifacts_ArtifactKey",
                        column: x => x.ArtifactKey,
                        principalTable: "Artifacts",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArtifactTags_ArtifactKey_Name",
                table: "ArtifactTags",
                columns: new[] { "ArtifactKey", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArtifactTags");
        }
    }
}
