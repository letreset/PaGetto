using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace PaGetto.Database.MySql;

public class MySqlContext : AbstractContext<MySqlContext>
{
    private readonly DatabaseOptions _pagettoOptions;

    /// <summary>
    /// The MySQL Server error code for when a unique constraint is violated.
    /// </summary>
    private const int UniqueConstraintViolationErrorCode = 1062;

    public const string CharSet = "utf8mb4";

    /// <summary>
    /// Case- and accent-insensitive, and available on MySQL 5.7, MySQL 8 and MariaDB.
    /// </summary>
    public const string Collation = "utf8mb4_unicode_ci";

    public MySqlContext(DbContextOptions<MySqlContext> efOptions, IOptionsSnapshot<PaGettoOptions> pagettoOptions) : base(efOptions)
    {
        _pagettoOptions = pagettoOptions.Value.Database;
    }

    public override bool IsUniqueConstraintViolationException(DbUpdateException exception)
    {
        return exception.InnerException is MySqlException mysqlException &&
               mysqlException.Number == UniqueConstraintViolationErrorCode;
    }

    /// <summary>
    /// MySQL does not support LIMIT clauses in subqueries for certain subquery operators.
    /// See: https://dev.mysql.com/doc/refman/8.0/en/subquery-restrictions.html
    /// </summary>
    public override bool SupportsLimitInSubqueries => false;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Apply the collation explicitly to every table and column (it implies the charset). A column-level
        // "CHARACTER SET utf8mb4" without a collation would get the server's default collation instead.
        modelBuilder.HasCharSet(CharSet, DelegationModes.ApplyToDatabases);
        modelBuilder.UseCollation(Collation, DelegationModes.ApplyToAll);

        base.OnModelCreating(modelBuilder);

        // With utf8mb4 (4 bytes per character) nine varchar(4000) columns exceed MySQL's 65535-byte row size
        // limit ("Row size too large"). TEXT only counts a few bytes towards that limit and still holds 4000 characters.
        modelBuilder.Entity<Package>(package =>
        {
            package.Property(p => p.Authors).HasColumnType("text");
            package.Property(p => p.CachedFrom).HasColumnType("text");
            package.Property(p => p.Copyright).HasColumnType("text");
            package.Property(p => p.Description).HasColumnType("text");
            package.Property(p => p.IconUrl).HasColumnType("text");
            package.Property(p => p.LicenseUrl).HasColumnType("text");
            package.Property(p => p.ProjectUrl).HasColumnType("text");
            package.Property(p => p.RepositoryUrl).HasColumnType("text");
            package.Property(p => p.Summary).HasColumnType("text");
            package.Property(p => p.Tags).HasColumnType("text");
        });
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if(!optionsBuilder.IsConfigured)
            optionsBuilder.UseMySql(_pagettoOptions.ConnectionString, MySqlServerVersionResolver.Resolve(_pagettoOptions));
    }
}
