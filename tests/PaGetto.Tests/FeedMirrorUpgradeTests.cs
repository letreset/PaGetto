using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using PaGetto.Core.Configuration;
using PaGetto.Database.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace PaGetto.Tests;

/// <summary>
/// Verifies that the AddFeedMirrors migration moves each feed's old Mirror* columns into a
/// FeedMirrors row on a pre-existing database, and that Down copies them back.
/// </summary>
public class FeedMirrorUpgradeTests : IDisposable
{
    private const string LastMigrationBeforeMirrors = "20260715094512_AddFeedSortOrder";

    private readonly string _dbPath;
    private readonly string _connectionString;

    public FeedMirrorUpgradeTests()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PaGettoMigrationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        _dbPath = Path.Combine(tempDir, "mirror-upgrade-test.db");
        _connectionString = $"Data Source={_dbPath};Pooling=False";
    }

    [Fact]
    public async Task MirrorColumns_AreMovedIntoFeedMirrors_AfterMigration()
    {
        await MigrateTo(LastMigrationBeforeMirrors);

        SeedFeed("enabled", mirrorEnabled: true, source: "https://api.nuget.org/v3/index.json",
            legacy: false, timeout: 300, authType: (int)MirrorAuthenticationType.Basic, username: "user", password: "secret");
        SeedFeed("disabled", mirrorEnabled: false, source: "https://vendor.test/v3/index.json",
            legacy: true, timeout: null, authType: null, username: null, password: null);
        SeedFeed("nosource", mirrorEnabled: true, source: null,
            legacy: false, timeout: null, authType: null, username: null, password: null);

        await MigrateTo(null);

        var rows = new Dictionary<string, object[]>();
        using (var conn = OpenConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT f.Slug, m.SortOrder, m.Enabled, m.PackageSource, m.Legacy, m.DownloadTimeoutSeconds,
                       m.AuthType, m.AuthUsername, m.AuthPassword
                FROM FeedMirrors m JOIN Feeds f ON f.Id = m.FeedId";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var values = new object[reader.FieldCount];
                reader.GetValues(values);
                rows[reader.GetString(0)] = values;
            }
        }

        Assert.Equal(2, rows.Count);
        Assert.Equal(
            new object[] { "enabled", 0L, 1L, "https://api.nuget.org/v3/index.json", 0L, 300L, (long)MirrorAuthenticationType.Basic, "user", "secret" },
            rows["enabled"]);
        Assert.Equal(
            new object[] { "disabled", 0L, 0L, "https://vendor.test/v3/index.json", 1L, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value },
            rows["disabled"]);
        Assert.False(rows.ContainsKey("nosource"));
    }

    [Fact]
    public async Task FirstMirror_IsCopiedBack_WhenMigratingDown()
    {
        await MigrateTo(null);

        var feedId = Guid.NewGuid().ToString().ToUpperInvariant();
        using (var conn = OpenConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                INSERT INTO Feeds (Id, Slug, Name, SortOrder, CreatedAtUtc, UpdatedAtUtc)
                VALUES ($id, 'two', 'Two', 1, '2020-01-01', '2020-01-01');
                INSERT INTO FeedMirrors (FeedId, SortOrder, Enabled, PackageSource, Legacy)
                VALUES ($id, 1, 1, 'https://second.test/v3/index.json', 0),
                       ($id, 0, 1, 'https://first.test/v3/index.json', 0);";
            cmd.Parameters.AddWithValue("$id", feedId);
            cmd.ExecuteNonQuery();
        }

        await MigrateTo(LastMigrationBeforeMirrors);

        using (var conn = OpenConnection())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT MirrorEnabled, MirrorPackageSource FROM Feeds WHERE Slug = 'two'";
            using var reader = cmd.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(1L, reader.GetInt64(0));
            Assert.Equal("https://first.test/v3/index.json", reader.GetString(1));
        }
    }

    private void SeedFeed(
        string slug, bool mirrorEnabled, string source, bool legacy, int? timeout,
        int? authType, string username, string password)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Feeds (Id, Slug, Name, SortOrder, MirrorEnabled, MirrorPackageSource, MirrorLegacy,
                               MirrorDownloadTimeoutSeconds, MirrorAuthType, MirrorAuthUsername, MirrorAuthPassword,
                               CreatedAtUtc, UpdatedAtUtc)
            VALUES ($id, $slug, $slug, 1, $enabled, $source, $legacy, $timeout, $authType, $username, $password,
                    '2020-01-01', '2020-01-01')";
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString().ToUpperInvariant());
        cmd.Parameters.AddWithValue("$slug", slug);
        cmd.Parameters.AddWithValue("$enabled", mirrorEnabled);
        cmd.Parameters.AddWithValue("$source", (object)source ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$legacy", legacy);
        cmd.Parameters.AddWithValue("$timeout", (object)timeout ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$authType", (object)authType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$username", (object)username ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$password", (object)password ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Migrates to the given migration, or to the latest one when null.</summary>
    private async Task MigrateTo(string targetMigration)
    {
        using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<SqliteContext>();
        await ctx.Database.GetService<IMigrator>().MigrateAsync(targetMigration);
    }

    private ServiceProvider BuildServiceProvider()
    {
        var pagettoOptions = new PaGettoOptions
        {
            Database = new DatabaseOptions { ConnectionString = _connectionString }
        };

        var snapshot = new Mock<IOptionsSnapshot<PaGettoOptions>>();
        snapshot.Setup(s => s.Value).Returns(pagettoOptions);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(snapshot.Object);
        services.AddDbContext<SqliteContext>(opts => opts.UseSqlite(_connectionString));

        return services.BuildServiceProvider();
    }

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }
}
