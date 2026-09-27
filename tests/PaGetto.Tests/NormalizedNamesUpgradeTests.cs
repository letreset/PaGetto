using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Database.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace PaGetto.Tests;

/// <summary>
/// Upgrading to AddNormalizedUserAndGroupNames on a database that already has users and groups.
/// </summary>
public class NormalizedNamesUpgradeTests : IDisposable
{
    private const string PreviousMigration = "20260926091417_AddPackageCopyrightLicenseExpressionSize";
    private const string LastMigrationBeforeAuth = "20260120125631_AddCachedFromToPackage";

    private readonly string _dbPath;
    private readonly string _connectionString;

    public NormalizedNamesUpgradeTests()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "PaGettoMigrationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        _dbPath = Path.Combine(tempDir, "normalized-names-upgrade-test.db");
        _connectionString = $"Data Source={_dbPath}";
    }

    [Fact]
    public async Task RefusesToMigrateNamesThatDifferOnlyInCase()
    {
        await ApplyMigrationsUptoAsync(PreviousMigration);
        SeedUser("alice");
        SeedUser("ALICE");
        SeedUser("bob");
        SeedGroup("Developers");
        SeedGroup("developers");

        using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SqliteContext>();

        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => context.RunMigrationsAsync(CancellationToken.None));

        Assert.Contains("'alice'", e.Message);
        Assert.Contains("'ALICE'", e.Message);
        Assert.Contains("'Developers'", e.Message);
        Assert.Contains("'developers'", e.Message);
        Assert.DoesNotContain("bob", e.Message);
        Assert.Contains(PreviousMigration, await context.Database.GetAppliedMigrationsAsync());
        Assert.DoesNotContain(await context.Database.GetAppliedMigrationsAsync(), m => m.EndsWith("_AddNormalizedUserAndGroupNames"));
    }

    [Fact]
    public async Task NormalizesExistingNamesIncludingNonAscii()
    {
        await ApplyMigrationsUptoAsync(PreviousMigration);
        SeedUser("émile");
        SeedUser("Bob");
        SeedGroup("Ürün");

        using var sp = BuildServiceProvider();
        using (var scope = sp.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<SqliteContext>().RunMigrationsAsync(CancellationToken.None);
        }

        using (var scope = sp.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<SqliteContext>();
            var users = new UserService(context, Options(), NullLogger<UserService>.Instance);
            var groups = new GroupService(context, NullLogger<GroupService>.Instance);

            Assert.Equal("émile", (await users.FindByUsernameAsync("ÉMILE", CancellationToken.None))?.Username);
            Assert.Equal("Bob", (await users.FindByUsernameAsync("bob", CancellationToken.None))?.Username);
            Assert.Equal("Ürün", (await groups.FindByNameAsync("ürün", CancellationToken.None))?.Name);
        }
    }

    [Fact]
    public async Task UpgradesADatabaseFromBeforeUsersAndGroups()
    {
        // The last migration before the Users and Groups tables exist.
        await ApplyMigrationsUptoAsync(LastMigrationBeforeAuth);

        using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SqliteContext>();

        await context.RunMigrationsAsync(CancellationToken.None);

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task RejectsANewNameThatDiffersOnlyInCase()
    {
        using var sp = BuildServiceProvider();
        using var scope = sp.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SqliteContext>();
        await context.RunMigrationsAsync(CancellationToken.None);
        var groups = new GroupService(context, NullLogger<GroupService>.Instance);

        await groups.CreateGroupAsync("Developers", null, null, CancellationToken.None);

        var e = await Assert.ThrowsAsync<DbUpdateException>(
            () => groups.CreateGroupAsync("DEVELOPERS", null, null, CancellationToken.None));
        Assert.True(context.IsUniqueConstraintViolationException(e));
    }

    private static IOptionsSnapshot<NugetAuthenticationOptions> Options()
    {
        var snapshot = new Mock<IOptionsSnapshot<NugetAuthenticationOptions>>();
        snapshot.Setup(s => s.Value).Returns(new NugetAuthenticationOptions());
        return snapshot.Object;
    }

    private void SeedUser(string username)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Users (Id, Username, DisplayName, AuthProvider, IsEnabled, IsAdmin, CanLoginToUI, FailedLoginCount, CreatedAtUtc, UpdatedAtUtc)
            VALUES ($id, $username, $username, 1, 1, 0, 1, 0, '2020-01-01', '2020-01-01')";
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString().ToUpperInvariant());
        cmd.Parameters.AddWithValue("$username", username);
        cmd.ExecuteNonQuery();
    }

    private void SeedGroup(string name)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO Groups (Id, Name, CreatedAtUtc) VALUES ($id, $name, '2020-01-01')";
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString().ToUpperInvariant());
        cmd.Parameters.AddWithValue("$name", name);
        cmd.ExecuteNonQuery();
    }

    private async Task ApplyMigrationsUptoAsync(string targetMigration)
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
        SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
    }
}
