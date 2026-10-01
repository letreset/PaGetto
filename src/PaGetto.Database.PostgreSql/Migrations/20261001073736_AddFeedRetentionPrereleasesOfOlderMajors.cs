using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaGetto.Database.PostgreSql.Migrations
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
                type: "boolean",
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
