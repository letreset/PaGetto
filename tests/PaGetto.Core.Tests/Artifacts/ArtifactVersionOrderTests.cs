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
        public void PrefersTheVersionOfTheLatestTag()
        {
            ArtifactVersion[] versions = [Version("1.0.0", listed: false), Version("2.0.0-beta.1", isPrerelease: true)];

            Assert.Equal("1.0.0", ArtifactVersionOrder.GetLatest(FeedType.Npm, versions, [Tag("latest", "1.0.0"), Tag("beta", "2.0.0-beta.1")]).Version);

            // A tag of a version that is gone, or another tag, is ignored.
            Assert.Equal("2.0.0-beta.1", ArtifactVersionOrder.GetLatest(FeedType.Npm, versions, [Tag("latest", "9.9.9")]).Version);
            Assert.Equal("2.0.0-beta.1", ArtifactVersionOrder.GetLatest(FeedType.Npm, versions, [Tag("next", "1.0.0")]).Version);
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
        public void SortsNpmVersionsByPrecedence()
        {
            var sorted = ArtifactVersionOrder.SortDescending(FeedType.Npm, [Version("1.0.0-beta.2"), Version("1.0.0"), Version("1.0.0-beta.10"), Version("0.10.0")]);

            Assert.Equal(new[] { "1.0.0", "1.0.0-beta.10", "1.0.0-beta.2", "0.10.0" }, sorted.Select(v => v.Version));
        }

        [Fact]
        public void SortsMavenVersionsAsMavenDoes()
        {
            var sorted = ArtifactVersionOrder.SortDescending(FeedType.Maven, [Version("1.0-SNAPSHOT"), Version("1.10"), Version("1.0"), Version("1.9-rc1"), Version("1.0-sp1")]);

            Assert.Equal(new[] { "1.10", "1.9-rc1", "1.0-sp1", "1.0", "1.0-SNAPSHOT" }, sorted.Select(v => v.Version));
        }

        [Fact]
        public void SortsPythonVersionsAsPep440Does()
        {
            var sorted = ArtifactVersionOrder.SortDescending(FeedType.PyPI, [Version("1.0rc1"), Version("1.10"), Version("1.0"), Version("1.0.post1"), Version("1.0.dev1"), Version("1.9")]);

            Assert.Equal(new[] { "1.10", "1.9", "1.0.post1", "1.0", "1.0rc1", "1.0.dev1" }, sorted.Select(v => v.Version));
        }

        [Fact]
        public void NuGetFeedsHaveNoArtifacts()
        {
            Assert.Throws<NotSupportedException>(() => ArtifactVersionOrder.GetComparer(FeedType.NuGet));
        }
    }

    private static ArtifactTag Tag(string name, string version)
    {
        return new ArtifactTag { Name = name, Version = version };
    }

    private static ArtifactVersion Version(string version, bool isPrerelease = false, bool listed = true)
    {
        return new ArtifactVersion { Version = version, IsPrerelease = isPrerelease, Listed = listed };
    }
}
