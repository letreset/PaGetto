using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Audit;
using PaGetto.Core.Entities;
using PaGetto.Core.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace PaGetto.Core.Tests.Audit;

public class AuditEventServiceTests
{
    public class AddAsync : FactsBase
    {
        [Fact]
        public async Task StoresTheEvent()
        {
            await Target.AddAsync(Event("feed_created", Day(1), target: "internal", detail: "name=Internal"), Ct);

            var stored = await Context.AuditEvents.SingleAsync(Ct);
            Assert.Equal("feed_created", stored.Event);
            Assert.Equal("internal", stored.Target);
            Assert.Equal("name=Internal", stored.Detail);
            Assert.Equal("admin", stored.Actor);
            Assert.Equal(Day(1), stored.TimestampUtc);
        }
    }

    public class SearchAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsNewestFirstWithTheTotalCount()
        {
            await AddAsync(Event("a", Day(1)), Event("b", Day(3)), Event("c", Day(2)));

            var (events, total) = await Target.SearchAsync(new AuditEventFilter(), 0, 10, Ct);

            Assert.Equal(3, total);
            Assert.Equal(["b", "c", "a"], events.Select(e => e.Event));
        }

        [Fact]
        public async Task Pages()
        {
            await AddAsync(Event("a", Day(1)), Event("b", Day(2)), Event("c", Day(3)), Event("d", Day(4)), Event("e", Day(5)));

            var (events, total) = await Target.SearchAsync(new AuditEventFilter(), 2, 2, Ct);

            Assert.Equal(5, total);
            Assert.Equal(["c", "b"], events.Select(e => e.Event));
        }

        [Fact]
        public async Task FiltersByEventActorAndFeed()
        {
            await AddAsync(
                Event("package_delete_succeeded", Day(1), actor: "alice", feed: "internal", packageId: "Foo"),
                Event("package_delete_succeeded", Day(2), actor: "bob", feed: "internal", packageId: "Foo"),
                Event("package_delete_succeeded", Day(3), actor: "alice", feed: "default", packageId: "Foo"),
                Event("package_upload_succeeded", Day(4), actor: "alice", feed: "internal", packageId: "Foo"));

            var (events, total) = await Target.SearchAsync(
                new AuditEventFilter { Event = "package_delete_succeeded", Actor = "lic", Feed = "internal" }, 0, 10, Ct);

            Assert.Equal(1, total);
            Assert.Equal(Day(1), Assert.Single(events).TimestampUtc);
        }

        [Fact]
        public async Task MatchesTheTargetOrThePackageId()
        {
            await AddAsync(
                Event("group_created", Day(1), target: "Contoso team"),
                Event("package_upload_succeeded", Day(2), packageId: "Contoso.Core"),
                Event("account_created", Day(3), target: "bob"));

            var (events, _) = await Target.SearchAsync(new AuditEventFilter { Target = "Contoso" }, 0, 10, Ct);

            Assert.Equal(["package_upload_succeeded", "group_created"], events.Select(e => e.Event));
        }

        [Fact]
        public async Task FiltersByTimeRange()
        {
            await AddAsync(Event("a", Day(1)), Event("b", Day(2)), Event("c", Day(3)));

            var (events, _) = await Target.SearchAsync(
                new AuditEventFilter { FromUtc = Day(2), BeforeUtc = Day(3) }, 0, 10, Ct);

            Assert.Equal("b", Assert.Single(events).Event);
        }
    }

    public class GetEventNamesAsync : FactsBase
    {
        [Fact]
        public async Task ReturnsDistinctSortedNames()
        {
            await AddAsync(Event("group_created", Day(1)), Event("account_created", Day(2)), Event("group_created", Day(3)));

            var names = await Target.GetEventNamesAsync(Ct);

            Assert.Equal(["account_created", "group_created"], names);
        }
    }

    public class DeleteOlderThanAsync : FactsBase
    {
        [Fact]
        public async Task DeletesOnlyOlderEvents()
        {
            await AddAsync(Event("a", Day(1)), Event("b", Day(2)), Event("c", Day(3)));

            var deleted = await Target.DeleteOlderThanAsync(Day(2), Ct);

            Assert.Equal(1, deleted);
            Assert.Equal(["b", "c"], await Context.AuditEvents.OrderBy(e => e.TimestampUtc).Select(e => e.Event).ToListAsync(Ct));
        }
    }

    public class FactsBase : IDisposable
    {
        protected readonly TestDbContext Context;
        protected readonly AuditEventService Target;
        protected readonly CancellationToken Ct = CancellationToken.None;

        protected FactsBase()
        {
            Context = TestDbContext.Create();
            Target = new AuditEventService(Context);
        }

        protected static DateTime Day(int day)
        {
            return new DateTime(2020, 1, day, 0, 0, 0, DateTimeKind.Utc);
        }

        protected static AuditEvent Event(
            string name,
            DateTime timestampUtc,
            string actor = "admin",
            string feed = null,
            string target = null,
            string detail = null,
            string packageId = null)
        {
            return new AuditEvent
            {
                Id = Guid.NewGuid(),
                TimestampUtc = timestampUtc,
                Event = name,
                Actor = actor,
                IpAddress = "10.0.0.1",
                Feed = feed,
                Target = target,
                Detail = detail,
                PackageId = packageId,
            };
        }

        protected async Task AddAsync(params AuditEvent[] events)
        {
            Context.AuditEvents.AddRange(events);
            await Context.SaveChangesAsync(Ct);
        }

        public void Dispose()
        {
            Context.Dispose();
        }
    }
}
