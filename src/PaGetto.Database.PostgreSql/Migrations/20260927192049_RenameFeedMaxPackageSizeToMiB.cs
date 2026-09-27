using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaGetto.Database.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class RenameFeedMaxPackageSizeToMiB : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "MaxPackageSizeGiB",
                table: "Feeds",
                newName: "MaxPackageSizeMiB");

            migrationBuilder.Sql("UPDATE \"Feeds\" SET \"MaxPackageSizeMiB\" = \"MaxPackageSizeMiB\" * 1024 WHERE \"MaxPackageSizeMiB\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"Feeds\" SET \"MaxPackageSizeMiB\" = \"MaxPackageSizeMiB\" / 1024 WHERE \"MaxPackageSizeMiB\" IS NOT NULL");

            migrationBuilder.RenameColumn(
                name: "MaxPackageSizeMiB",
                table: "Feeds",
                newName: "MaxPackageSizeGiB");
        }
    }
}
