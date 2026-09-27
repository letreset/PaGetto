using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Configuration;
using PaGetto.Database.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace PaGetto.Tests;

public class SqliteContextTests
{
    public class RunMigrationsAsync : FactsBase
    {
        [Fact]
        public async Task AppliesWalJournalMode_ToExistingDatabase()
        {
            CreateDatabaseWithJournalMode("DELETE");

            await RunMigrations("WAL");

            using var conn = OpenConnection();
            Assert.Equal("wal", GetJournalMode(conn));
            Assert.True(File.Exists(DbPath + "-wal"));
            Assert.True(File.Exists(DbPath + "-shm"));
        }

        [Fact]
        public async Task AppliesDeleteJournalMode_ToNewDatabase()
        {
            // EF Core creates new SQLite databases in WAL mode, so this switches it back.
            await RunMigrations("delete");

            using var conn = OpenConnection();
            Assert.Equal("delete", GetJournalMode(conn));
            Assert.False(File.Exists(DbPath + "-wal"));
        }

        [Fact]
        public async Task KeepsExistingJournalMode_WhenNotConfigured()
        {
            CreateDatabaseWithJournalMode("DELETE");

            await RunMigrations(null);

            using var conn = OpenConnection();
            Assert.Equal("delete", GetJournalMode(conn));
            Assert.False(File.Exists(DbPath + "-wal"));
        }
    }

    public abstract class FactsBase : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _connectionString;

        protected FactsBase()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "PaGettoSqliteContextTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            DbPath = Path.Combine(_tempDir, "journal-mode-test.db");
            _connectionString = $"Data Source={DbPath};Pooling=False";
        }

        protected string DbPath { get; }

        protected void CreateDatabaseWithJournalMode(string journalMode)
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA journal_mode={journalMode}; CREATE TABLE Seed (Id INTEGER);";
            cmd.ExecuteNonQuery();
        }

        protected async Task RunMigrations(string journalMode)
        {
            var pagettoOptions = new PaGettoOptions
            {
                Database = new DatabaseOptions { ConnectionString = _connectionString, JournalMode = journalMode }
            };

            var snapshot = new Mock<IOptionsSnapshot<PaGettoOptions>>();
            snapshot.Setup(s => s.Value).Returns(pagettoOptions);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(snapshot.Object);
            services.AddDbContext<SqliteContext>(opts => opts.UseSqlite(_connectionString));

            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<SqliteContext>();
            await ctx.RunMigrationsAsync(CancellationToken.None);
        }

        protected SqliteConnection OpenConnection()
        {
            var conn = new SqliteConnection(_connectionString);
            conn.Open();
            return conn;
        }

        protected static string GetJournalMode(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode;";
            return (string)cmd.ExecuteScalar();
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
