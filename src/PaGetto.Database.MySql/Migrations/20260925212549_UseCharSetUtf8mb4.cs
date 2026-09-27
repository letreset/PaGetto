using System.Linq;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PaGetto.Database.MySql.Migrations;

/// <summary>
/// Converts the database and all tables from latin1 to utf8mb4, keeping the data.
/// Each table is rewritten once by a single ALTER TABLE. The char(36) Guid columns keep their ascii collation,
/// which is why this doesn't use "CONVERT TO CHARACTER SET".
/// </summary>
public partial class UseCharSetUtf8mb4 : Migration
{
    private const string OldCharSet = "latin1";
    private const string OldCollation = "latin1_swedish_ci";

    /// <summary>
    /// Tables without string columns; only their default charset changes.
    /// </summary>
    private static readonly string[] TablesWithoutStringColumns = ["FeedPermissions", "UserGroups"];

    /// <summary>
    /// Every string column: its type after this migration, its type before, and whether it's nullable.
    /// </summary>
    private static readonly (string Table, string Column, string Type, string OldType, bool Nullable)[] StringColumns =
    [
        ("FeedMirrors", "AuthCustomHeaders", "longtext", "longtext", true),
        ("FeedMirrors", "AuthPassword", "longtext", "longtext", true),
        ("FeedMirrors", "AuthToken", "longtext", "longtext", true),
        ("FeedMirrors", "AuthUsername", "longtext", "longtext", true),
        ("FeedMirrors", "PackageSource", "longtext", "longtext", false),
        ("Feeds", "Description", "varchar(4000)", "varchar(4000)", true),
        ("Feeds", "Name", "varchar(256)", "varchar(256)", false),
        ("Feeds", "Slug", "varchar(128)", "varchar(128)", false),
        ("Groups", "AppRoleValue", "varchar(128)", "varchar(128)", true),
        ("Groups", "Description", "varchar(4000)", "varchar(4000)", true),
        ("Groups", "Name", "varchar(256)", "varchar(256)", false),
        ("PackageDependencies", "Id", "varchar(128)", "varchar(128)", true),
        ("PackageDependencies", "TargetFramework", "varchar(256)", "varchar(256)", true),
        ("PackageDependencies", "VersionRange", "varchar(256)", "varchar(256)", true),
        ("PackageTypes", "Name", "varchar(512)", "varchar(512)", true),
        ("PackageTypes", "Version", "varchar(64)", "varchar(64)", true),
        // Nine varchar(4000) columns don't fit MySQL's 65535-byte row size limit with utf8mb4, so they become text.
        ("Packages", "Authors", "text", "varchar(4000)", true),
        ("Packages", "CachedFrom", "text", "varchar(4000)", true),
        ("Packages", "Description", "text", "varchar(4000)", true),
        ("Packages", "IconUrl", "text", "varchar(4000)", true),
        ("Packages", "Id", "varchar(128)", "varchar(128)", false),
        ("Packages", "Language", "varchar(20)", "varchar(20)", true),
        ("Packages", "LicenseUrl", "text", "varchar(4000)", true),
        ("Packages", "MinClientVersion", "varchar(44)", "varchar(44)", true),
        ("Packages", "OriginalVersion", "varchar(64)", "varchar(64)", true),
        ("Packages", "ProjectUrl", "text", "varchar(4000)", true),
        ("Packages", "ReleaseNotes", "longtext", "longtext", true),
        ("Packages", "RepositoryType", "varchar(100)", "varchar(100)", true),
        ("Packages", "RepositoryUrl", "text", "varchar(4000)", true),
        ("Packages", "Summary", "text", "varchar(4000)", true),
        ("Packages", "Tags", "text", "varchar(4000)", true),
        ("Packages", "Title", "varchar(256)", "varchar(256)", true),
        ("Packages", "Version", "varchar(64)", "varchar(64)", false),
        ("PersonalAccessTokens", "Name", "varchar(256)", "varchar(256)", false),
        ("PersonalAccessTokens", "TokenHash", "varchar(128)", "varchar(128)", false),
        ("PersonalAccessTokens", "TokenPrefix", "varchar(8)", "varchar(8)", false),
        ("TargetFrameworks", "Moniker", "varchar(256)", "varchar(256)", true),
        ("Users", "DisplayName", "varchar(256)", "varchar(256)", false),
        ("Users", "Email", "varchar(256)", "varchar(256)", true),
        ("Users", "EntraObjectId", "varchar(128)", "varchar(128)", true),
        ("Users", "PasswordHash", "varchar(256)", "varchar(256)", true),
        ("Users", "Username", "varchar(256)", "varchar(256)", false),
    ];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ConvertTo(migrationBuilder, MySqlContext.CharSet, MySqlContext.Collation, useOldTypes: false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Fails (in strict mode) instead of silently replacing characters if a value doesn't fit into latin1.
        ConvertTo(migrationBuilder, OldCharSet, OldCollation, useOldTypes: true);
    }

    private static void ConvertTo(MigrationBuilder migrationBuilder, string charSet, string collation, bool useOldTypes)
    {
        migrationBuilder.Sql($"ALTER DATABASE CHARACTER SET {charSet} COLLATE {collation};");

        // DYNAMIC allows index keys of up to 3072 bytes; utf8mb4 needs more than COMPACT's 767 bytes.
        var tableOptions = $"CHARACTER SET {charSet} COLLATE {collation}, ROW_FORMAT=DYNAMIC";

        foreach (var table in TablesWithoutStringColumns)
        {
            migrationBuilder.Sql($"ALTER TABLE `{table}` {tableOptions};");
        }

        foreach (var table in StringColumns.GroupBy(c => c.Table))
        {
            var modifications = table.Select(c =>
                $"MODIFY `{c.Column}` {(useOldTypes ? c.OldType : c.Type)} CHARACTER SET {charSet} COLLATE {collation} {(c.Nullable ? "NULL" : "NOT NULL")}");

            migrationBuilder.Sql($"ALTER TABLE `{table.Key}` {tableOptions}, {string.Join(", ", modifications)};");
        }
    }
}
