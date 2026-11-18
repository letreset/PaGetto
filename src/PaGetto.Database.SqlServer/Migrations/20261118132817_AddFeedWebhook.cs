using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaGetto.Database.SqlServer.Migrations
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
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebhookUrl",
                table: "Feeds",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);
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
