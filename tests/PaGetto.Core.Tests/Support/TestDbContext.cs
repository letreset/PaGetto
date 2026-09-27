using PaGetto.Core.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace PaGetto.Core.Tests.Support;

public class TestDbContext : AbstractContext<TestDbContext>
{
    public TestDbContext(DbContextOptions<TestDbContext> options)
        : base(options)
    { }

    public override bool IsUniqueConstraintViolationException(DbUpdateException exception)
    {
        // SQLITE_CONSTRAINT, the same check SqliteContext does.
        return exception.InnerException is SqliteException { SqliteErrorCode: 19 };
    }

    public static TestDbContext Create()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        var context = new TestDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();
        return context;
    }
}
