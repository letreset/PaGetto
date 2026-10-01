using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaGetto.Database.MySql.Migrations
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
                type: "tinyint(1)",
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
