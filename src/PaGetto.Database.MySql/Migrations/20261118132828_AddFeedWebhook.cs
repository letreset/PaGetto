using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaGetto.Database.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedWebhook : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WebhookSecret",
                table: "Feeds",
                type: "varchar(256)",
                maxLength: 256,
                nullable: true,
                collation: "utf8mb4_unicode_ci");

            migrationBuilder.AddColumn<string>(
                name: "WebhookUrl",
                table: "Feeds",
                type: "varchar(4000)",
                maxLength: 4000,
                nullable: true,
                collation: "utf8mb4_unicode_ci");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WebhookSecret",
                table: "Feeds");

            migrationBuilder.DropColumn(
                name: "WebhookUrl",
                table: "Feeds");
        }
    }
}
