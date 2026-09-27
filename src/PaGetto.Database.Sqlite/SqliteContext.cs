using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PaGetto.Database.Sqlite;

public partial class SqliteContext : AbstractContext<SqliteContext>
{
    private readonly DatabaseOptions _pagettoOptions;
    private readonly ILogger<SqliteContext> _logger;

    /// <summary>
    /// The Sqlite error code for when a unique constraint is violated.
    /// </summary>
    private const int SqliteUniqueConstraintViolationErrorCode = 19;

    public SqliteContext(
        DbContextOptions<SqliteContext> efOptions,
        IOptionsSnapshot<PaGettoOptions> pagettoOptions,
        ILogger<SqliteContext> logger)
        : base(efOptions)
    {
        _pagettoOptions = pagettoOptions.Value.Database;
        _logger = logger;
    }

    public override bool IsUniqueConstraintViolationException(DbUpdateException exception)
    {
        return exception.InnerException is SqliteException sqliteException &&
            sqliteException.SqliteErrorCode == SqliteUniqueConstraintViolationErrorCode;
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Package>()
            .Property(p => p.Id)
            .HasColumnType("TEXT COLLATE NOCASE");

        builder.Entity<Package>()
            .Property(p => p.NormalizedVersionString)
            .HasColumnType("TEXT COLLATE NOCASE");

        builder.Entity<PackageDependency>()
            .Property(d => d.Id)
            .HasColumnType("TEXT COLLATE NOCASE");

        builder.Entity<PackageType>()
            .Property(t => t.Name)
            .HasColumnType("TEXT COLLATE NOCASE");

        builder.Entity<TargetFramework>()
            .Property(f => f.Moniker)
            .HasColumnType("TEXT COLLATE NOCASE");
    }

    public override async Task RunMigrationsAsync(CancellationToken cancellationToken)
    {
        if (Database.GetDbConnection() is SqliteConnection connection)
        {
            /* Create the folder of the Sqlite blob if it does not exist. */
            EnsureDataSourceDirectoryExists(connection);
        }

        await base.RunMigrationsAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(_pagettoOptions?.JournalMode))
        {
            await ApplyJournalModeAsync(_pagettoOptions.JournalMode, cancellationToken);
        }
    }

    /// <summary>
    /// Sets the journal mode configured in Database:JournalMode. SQLite stores it in the database file,
    /// so it persists across connections.
    /// </summary>
    private async Task ApplyJournalModeAsync(string configuredMode, CancellationToken cancellationToken)
    {
        // Only a known mode from the allow-list ever reaches the SQL, never the raw config value.
        var journalMode = DatabaseOptions.SqliteJournalModes
            .FirstOrDefault(m => string.Equals(m, configuredMode, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Unsupported SQLite journal mode '{configuredMode}'. " +
                $"Allowed values: {string.Join(", ", DatabaseOptions.SqliteJournalModes)}");

        await Database.OpenConnectionAsync(cancellationToken);
        try
        {
            using var command = Database.GetDbConnection().CreateCommand();
            command.CommandText = $"PRAGMA journal_mode={journalMode};";
            var resultingMode = await command.ExecuteScalarAsync(cancellationToken) as string ?? string.Empty;

            if (string.Equals(resultingMode, journalMode, StringComparison.OrdinalIgnoreCase))
            {
                LogJournalModeApplied(resultingMode);
            }
            else
            {
                LogJournalModeNotApplied(journalMode, resultingMode);
            }
        }
        finally
        {
            await Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Creates directories specified in the Database::ConnectionString config for the Sqlite database file.
    /// </summary>
    /// <param name="connection">Instance of the <see cref="SqliteConnection"/>.</param>
    private static void EnsureDataSourceDirectoryExists(SqliteConnection connection)
    {
        var pathToCreate = Path.GetDirectoryName(connection.DataSource);

        if (string.IsNullOrWhiteSpace(pathToCreate)) return;

        Directory.CreateDirectory(pathToCreate);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
            optionsBuilder.UseSqlite(_pagettoOptions.ConnectionString);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "SQLite journal mode is {JournalMode}")]
    private partial void LogJournalModeApplied(string journalMode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "SQLite journal mode {RequestedJournalMode} was not applied, the database reports {JournalMode}")]
    private partial void LogJournalModeNotApplied(string requestedJournalMode, string journalMode);
}
