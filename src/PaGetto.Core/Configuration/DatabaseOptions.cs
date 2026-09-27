using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace PaGetto.Core.Configuration;

public class DatabaseOptions
{
    public string Type { get; set; }

    [Required]
    public string ConnectionString { get; set; }

    /// <summary>
    /// MySQL only: the server version (e.g. <c>8.0.36-mysql</c>). When set, the version is not auto-detected
    /// from the server.
    /// </summary>
    public string ServerVersion { get; set; }

    /// <summary>
    /// SQLite only: the journal mode (one of <see cref="SqliteJournalModes"/>, case-insensitive) applied with
    /// <c>PRAGMA journal_mode</c> after migrations. When not set, the database keeps its current journal mode.
    /// </summary>
    public string JournalMode { get; set; }

    /// <summary>
    /// The journal modes SQLite supports, see https://www.sqlite.org/pragma.html#pragma_journal_mode.
    /// </summary>
    public static IReadOnlyList<string> SqliteJournalModes { get; } =
        ["DELETE", "TRUNCATE", "PERSIST", "MEMORY", "WAL", "OFF"];
}
