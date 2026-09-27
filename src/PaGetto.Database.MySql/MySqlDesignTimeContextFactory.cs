using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using PaGetto.Core.Configuration;
using Microsoft.Extensions.Options;

namespace PaGetto.Database.MySql;

/// <summary>
/// Design-time factory for MySqlContext, used by EF Core tools to generate migrations
/// without requiring a live MySQL connection.
/// </summary>
public class MySqlDesignTimeContextFactory : IDesignTimeDbContextFactory<MySqlContext>
{
    public MySqlContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<MySqlContext>();
        optionsBuilder.UseMySql(
            "Server=localhost;Database=pagetto_design;",
            new MySqlServerVersion(new System.Version(8, 0, 0)));

        var pagettoOptions = Options.Create(new PaGettoOptions
        {
            Database = new DatabaseOptions
            {
                Type = "MySql",
                ConnectionString = "Server=localhost;Database=pagetto_design;"
            }
        });

        return new MySqlContext(optionsBuilder.Options, new OptionsSnapshotWrapper<PaGettoOptions>(pagettoOptions.Value));
    }

    /// <summary>
    /// Minimal IOptionsSnapshot wrapper for design-time use only.
    /// </summary>
    private class OptionsSnapshotWrapper<T> : IOptionsSnapshot<T> where T : class, new()
    {
        public OptionsSnapshotWrapper(T value) => Value = value;
        public T Value { get; }
        public T Get(string name) => Value;
    }
}
