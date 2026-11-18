using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Diagnostics;
using PaGetto.Core.Entities;
using Xunit;

namespace PaGetto.Core.Tests.Diagnostics;

/// <summary>
/// The meter is static and other tests record on it at the same time, so these tests look for
/// their own measurements among all of them.
/// </summary>
public class PaGettoMetricsTests
{
    public class Instruments : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentBag<(string Name, long Value, Dictionary<string, object> Tags)> _measurements = [];

        public Instruments()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == PaGettoMetrics.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
                _measurements.Add((instrument.Name, value, tags.ToArray().ToDictionary(t => t.Key, t => t.Value))));
            _listener.Start();
        }

        [Fact]
        public async Task IndexLookupsRecordHitsAndMisses()
        {
            using var cache = new ArtifactIndexCache();
            var feed = new Feed { Id = Guid.NewGuid() };

            for (var i = 0; i < 2; i++)
            {
                await cache.GetOrBuildAsync(feed, "versions", TimeSpan.FromMinutes(1), () => Task.FromResult(Encoding.UTF8.GetBytes("x")));
            }

            Assert.Contains(_measurements, m => m.Name == "pagetto.index.lookups" && (string)m.Tags["pagetto.cache.result"] == "miss");
            Assert.Contains(_measurements, m => m.Name == "pagetto.index.lookups" && (string)m.Tags["pagetto.cache.result"] == "hit");
        }

        [Fact]
        public async Task StoredVersionsRecordTheirFeedTypeAndSource()
        {
            var artifacts = new Mock<IArtifactDatabase>();
            artifacts
                .Setup(a => a.AddVersionAsync(It.IsAny<Guid>(), "ext", It.IsAny<ArtifactVersion>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ArtifactAddResult.Success);
            using var archive = new MemoryStream([1]);

            await ArtifactPublisher.AddAsync(
                artifacts.Object,
                Mock.Of<IArtifactStorageService>(),
                new Feed { Id = Guid.NewGuid(), Slug = "vs", Type = FeedType.Vsix },
                "ext",
                new ArtifactVersion { Version = "1.0", CachedFrom = "https://gallery.test/" },
                archive,
                readme: null,
                default);

            Assert.Contains(_measurements, m => m.Name == "pagetto.packages.stored"
                && (string)m.Tags["pagetto.feed.type"] == "Vsix"
                && (string)m.Tags["pagetto.package.source"] == "mirrored");
        }

        [Fact]
        public void RemovalsRecordTheirCountAndReason()
        {
            PaGettoMetrics.RecordPackagesRemoved(3, unusedMirror: true);
            PaGettoMetrics.RecordPackagesRemoved(0, unusedMirror: false);

            Assert.Contains(_measurements, m => m.Name == "pagetto.packages.removed" && m.Value == 3
                && (string)m.Tags["pagetto.removal.reason"] == "unused_mirror");
        }

        public void Dispose()
        {
            _listener.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
