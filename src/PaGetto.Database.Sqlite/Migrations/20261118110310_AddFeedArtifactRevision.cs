using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaGetto.Database.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedArtifactRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ArtifactRevision",
                table: "Feeds",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArtifactRevision",
                table: "Feeds");
        }
    }
}
