using System;
using System.Linq;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Entities;
using Xunit;

namespace PaGetto.Core.Tests.Artifacts;

public class ArtifactVersionOrderTests
{
    public class GetLatest
    {
        [Fact]
        public void PrefersTheNewestListedStableVersion()
        {
            var latest = ArtifactVersionOrder.GetLatest(FeedType.Pub,
            [
                Version("1.0.0"),
                Version("1.10.0"),
                Version("1.9.0"),
                Version("2.0.0-dev.1", isPrerelease: true),
                Version("3.0.0", listed: false),
            ]);

            Assert.Equal("1.10.0", latest.Version);
        }

        [Fact]
        public void FallsBackToAPrereleaseAndThenToAnUnlistedVersion()
        {
            Assert.Equal("2.0.0-dev.1", ArtifactVersionOrder.GetLatest(FeedType.Pub,
            [
                Version("1.0.0-dev.1", isPrerelease: true),
                Version("2.0.0-dev.1", isPrerelease: true),
                Version("3.0.0", listed: false),
            ]).Version);

            Assert.Equal("3.0.0", ArtifactVersionOrder.GetLatest(FeedType.Pub, [Version("3.0.0", listed: false)]).Version);
        }

        [Fact]
        public void ReturnsNullWithoutVersions()
        {
            Assert.Null(ArtifactVersionOrder.GetLatest(FeedType.Pub, []));
        }
    }

    public class SortDescending
    {
        [Fact]
        public void SortsNewestFirst()
        {
            var sorted = ArtifactVersionOrder.SortDescending(FeedType.Pub, [Version("1.0.0"), Version("1.0.0+2"), Version("0.9.0")]);

            Assert.Equal(new[] { "1.0.0+2", "1.0.0", "0.9.0" }, sorted.Select(v => v.Version));
        }

        [Fact]
        public void NuGetFeedsHaveNoArtifacts()
        {
            Assert.Throws<NotSupportedException>(() => ArtifactVersionOrder.GetComparer(FeedType.NuGet));
        }
    }

    private static ArtifactVersion Version(string version, bool isPrerelease = false, bool listed = true)
    {
        return new ArtifactVersion { Version = version, IsPrerelease = isPrerelease, Listed = listed };
    }
}
