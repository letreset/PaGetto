using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaGetto.Database.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddPackageCopyrightLicenseExpressionSize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Copyright",
                table: "Packages",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LicenseExpression",
                table: "Packages",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Size",
                table: "Packages",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Copyright",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "LicenseExpression",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "Size",
                table: "Packages");
        }
    }
}
