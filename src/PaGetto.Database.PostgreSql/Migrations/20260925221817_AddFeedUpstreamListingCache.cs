using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaGetto.Database.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedUpstreamListingCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "UpstreamListingCacheSeconds",
                table: "Feeds",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UpstreamListingCacheSeconds",
                table: "Feeds");
        }
    }
}
