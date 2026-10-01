using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaGetto.Database.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedRetentionPrereleasesOfOlderMajors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RetentionDeletePrereleasesOfOlderMajors",
                table: "Feeds",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RetentionDeletePrereleasesOfOlderMajors",
                table: "Feeds");
        }
    }
}
