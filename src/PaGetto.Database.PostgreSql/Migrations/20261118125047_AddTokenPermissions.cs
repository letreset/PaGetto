using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaGetto.Database.PostgreSql.Migrations
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
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "CanPush",
                table: "PersonalAccessTokens",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FeedId",
                table: "PersonalAccessTokens",
                type: "uuid",
                nullable: true);
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
