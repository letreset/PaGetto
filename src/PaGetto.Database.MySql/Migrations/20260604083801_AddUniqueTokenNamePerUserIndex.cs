using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaGetto.Database.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueTokenNamePerUserIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // MySQL refuses to drop the only index backing FK_PersonalAccessTokens_Users_UserId,
            // so create the new (UserId, Name) index first. It can back the foreign key too.
            migrationBuilder.CreateIndex(
                name: "IX_PersonalAccessTokens_UserId_Name",
                table: "PersonalAccessTokens",
                columns: new[] { "UserId", "Name" },
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_PersonalAccessTokens_UserId",
                table: "PersonalAccessTokens");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PersonalAccessTokens_UserId",
                table: "PersonalAccessTokens",
                column: "UserId");

            migrationBuilder.DropIndex(
                name: "IX_PersonalAccessTokens_UserId_Name",
                table: "PersonalAccessTokens");
        }
    }
}
