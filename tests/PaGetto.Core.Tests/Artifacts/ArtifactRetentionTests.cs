using System.Linq;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using Xunit;

namespace PaGetto.Core.Tests.Artifacts;

public class ArtifactRetentionTests
{
    public class GetVersionsToRemove
    {
        [Fact]
        public void RemovesNothingWithoutLimits()
        {
            var removed = Remove(FeedType.Npm, new RetentionOptions(), "1.0.0", "2.0.0");

            Assert.Empty(removed);
        }

        [Fact]
        public void KeepsTheNewestMajors()
        {
            var removed = Remove(FeedType.Npm, new RetentionOptions { MaxMajorVersions = 2 }, "1.0.0", "1.1.0", "2.0.0", "3.0.0", "3.1.0");

            Assert.Equal(new[] { "1.0.0", "1.1.0" }, removed);
        }

        [Fact]
        public void KeepsTheNewestMinorsOfEachMajor()
        {
            var removed = Remove(FeedType.Cargo, new RetentionOptions { MaxMinorVersions = 1 }, "1.0.0", "1.1.0", "1.1.1", "2.0.0", "2.3.0");

            Assert.Equal(new[] { "1.0.0", "2.0.0" }, removed);
        }

        [Fact]
        public void KeepsTheNewestPatchesOfEachMinor()
        {
            var removed = Remove(FeedType.Pub, new RetentionOptions { MaxPatchVersions = 2 }, "1.0.0", "1.0.1", "1.0.2", "1.1.0");

            Assert.Equal(new[] { "1.0.0" }, removed);
        }

        [Fact]
        public void KeepsTheNewestPrereleasesOfEachPatchInTheEcosystemsOrder()
        {
            var removed = Remove(
                FeedType.Npm,
                new RetentionOptions { MaxPrereleaseVersions = 2 },
                "1.0.0-beta.1", "1.0.0-beta.2", "1.0.0-beta.10", "1.0.0", "1.1.0-rc.1");

            Assert.Equal(new[] { "1.0.0-beta.1" }, removed);
        }

        [Fact]
        public void RemovesPrereleasesOfOlderMajors()
        {
            var removed = Remove(
                FeedType.Npm,
                new RetentionOptions { DeletePrereleasesOfOlderMajors = true },
                "1.0.0-alpha", "2.0.0", "2.1.0-beta.1", "3.0.0-alpha");

            Assert.Equal(new[] { "1.0.0-alpha" }, removed);
        }

        [Fact]
        public void ReadsTheNumbersOfOtherVersionSchemes()
        {
            // Go's v prefix, R's dash and a two-part Maven version.
            Assert.Equal(new[] { "v1.0.0" }, Remove(FeedType.Go, new RetentionOptions { MaxMajorVersions = 1 }, "v1.0.0", "v2.0.0"));
            Assert.Equal(new[] { "1.0-1" }, Remove(FeedType.Cran, new RetentionOptions { MaxPatchVersions = 1 }, "1.0-1", "1.0-2"));
            Assert.Equal(new[] { "1.0" }, Remove(FeedType.Maven, new RetentionOptions { MaxMinorVersions = 1 }, "1.0", "1.1"));
        }

        [Fact]
        public void KeepsVersionsThatDontStartWithANumber()
        {
            var removed = Remove(FeedType.Composer, new RetentionOptions { MaxMajorVersions = 1 }, "dev-main", "1.0.0", "2.0.0");

            Assert.Equal(new[] { "1.0.0" }, removed);
        }

        [Fact]
        public void AlwaysKeepsTheNewestVersion()
        {
            var removed = Remove(FeedType.Npm, new RetentionOptions { MaxMajorVersions = 0 }, "1.0.0", "2.0.0");

            Assert.Equal(new[] { "1.0.0" }, removed);
        }

        private static string[] Remove(FeedType feedType, RetentionOptions options, params string[] versions)
        {
            var artifactVersions = versions
                .Select(v => new ArtifactVersion { Version = v, IsPrerelease = v.Contains('-', System.StringComparison.Ordinal) && !char.IsDigit(v[v.IndexOf('-', System.StringComparison.Ordinal) + 1]) })
                .ToList();

            return ArtifactRetention.GetVersionsToRemove(feedType, artifactVersions, options)
                .Select(v => v.Version)
                .OrderBy(v => versions.ToList().IndexOf(v))
                .ToArray();
        }
    }
}
