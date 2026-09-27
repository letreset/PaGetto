using System;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Audit;
using PaGetto.Core.Configuration;
using PaGetto.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace PaGetto.Core.Tests.Audit;

public class AuditRetentionServiceTests
{
    public class RunOnceAsync : FactsBase
    {
        [Fact]
        public async Task DeletesEventsOlderThanTheRetention()
        {
            AuditEvents
                .Setup(a => a.DeleteOlderThanAsync(Now.AddDays(-30), It.IsAny<CancellationToken>()))
                .ReturnsAsync(7);

            var deleted = await Build(retentionDays: 30).RunOnceAsync(CancellationToken.None);

            Assert.Equal(7, deleted);
        }
    }

    public class ExecuteAsync : FactsBase
    {
        [Fact]
        public async Task DoesNothingWhenRetentionIsOff()
        {
            var target = Build(retentionDays: 0);

            await target.StartAsync(CancellationToken.None);
            await target.ExecuteTask!;

            AuditEvents.Verify(a => a.DeleteOlderThanAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task CleansUpAtStartup()
        {
            var cleaned = new TaskCompletionSource();
            AuditEvents
                .Setup(a => a.DeleteOlderThanAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .Callback(() => cleaned.TrySetResult())
                .ReturnsAsync(0);
            var target = Build(retentionDays: 30);

            await target.StartAsync(CancellationToken.None);
            await cleaned.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await target.StopAsync(CancellationToken.None);

            AuditEvents.Verify(a => a.DeleteOlderThanAsync(Now.AddDays(-30), It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    public class FactsBase
    {
        protected static readonly DateTime Now = new(2020, 1, 31, 8, 0, 0, DateTimeKind.Utc);

        protected readonly Mock<IAuditEventService> AuditEvents = new();

        protected AuditRetentionService Build(int retentionDays)
        {
            var services = new ServiceCollection();
            services.AddSingleton(AuditEvents.Object);

            var time = new Mock<SystemTime>();
            time.Setup(t => t.UtcNow).Returns(Now);

            return new AuditRetentionService(
                services.BuildServiceProvider(),
                Options.Create(new AuditOptions { RetentionDays = retentionDays }),
                time.Object,
                NullLogger<AuditRetentionService>.Instance);
        }
    }
}
