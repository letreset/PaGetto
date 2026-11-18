using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaGetto.Database.MySql.Migrations
{
    /// <inheritdoc />
    public partial class AddTokenPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing tokens keep everything their user may do.
            migrationBuilder.AddColumn<bool>(
                name: "CanDelete",
                table: "PersonalAccessTokens",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "CanPush",
                table: "PersonalAccessTokens",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FeedId",
                table: "PersonalAccessTokens",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanDelete",
                table: "PersonalAccessTokens");

            migrationBuilder.DropColumn(
                name: "CanPush",
                table: "PersonalAccessTokens");

            migrationBuilder.DropColumn(
                name: "FeedId",
                table: "PersonalAccessTokens");
        }
    }
}
