using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaGetto.Database.MySql.Migrations
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
                    Key = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ArtifactKey = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_unicode_ci"),
                    Version = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, collation: "utf8mb4_unicode_ci")
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
                })
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

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
