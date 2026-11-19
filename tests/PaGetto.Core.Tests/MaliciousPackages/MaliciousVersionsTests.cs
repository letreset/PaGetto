using System.Text.Json;
using PaGetto.Core.Entities;
using PaGetto.Core.MaliciousPackages;
using Xunit;

namespace PaGetto.Core.Tests.MaliciousPackages;

public class MaliciousVersionsTests
{
    public class Read
    {
        [Theory]
        [InlineData("""{ "ranges": [{ "type": "SEMVER", "events": [{ "introduced": "0" }] }] }""")]
        [InlineData("""{ "package": { "ecosystem": "npm", "name": "x" } }""")]
        [InlineData("""{ "ranges": [{ "type": "GIT", "events": [{ "introduced": "abc" }] }] }""")]
        public void ReturnsNullWhenEveryVersionIsAffected(string affected)
        {
            using var json = JsonDocument.Parse(affected);

            Assert.Null(MaliciousVersions.Read(json.RootElement));
        }

        [Fact]
        public void KeepsTheListedVersionsAndRanges()
        {
            using var json = JsonDocument.Parse("""
                {
                  "versions": ["1.0.0", "1.0.0", "1.0.1"],
                  "ranges": [{ "type": "SEMVER", "events": [{ "introduced": "2.0.0" }, { "fixed": "2.1.0" }] }]
                }
                """);

            Assert.Equal(
                """{"versions":["1.0.0","1.0.1"],"ranges":[[{"introduced":"2.0.0"},{"fixed":"2.1.0"}]]}""",
                MaliciousVersions.Read(json.RootElement));
        }
    }

    public class IsAffected
    {
        private const string Stored = """{"versions":["0.5.0"],"ranges":[[{"introduced":"2.0.0"},{"fixed":"2.1.0"}],[{"introduced":"3.0.0"},{"last_affected":"3.2.0"}]]}""";

        [Theory]
        [InlineData("0.5.0", true)]
        [InlineData("1.0.0", false)]
        [InlineData("2.0.0", true)]
        [InlineData("2.0.9", true)]
        [InlineData("2.1.0", false)]
        [InlineData("3.2.0", true)]
        [InlineData("3.2.1", false)]
        public void MatchesListedVersionsAndRanges(string version, bool expected)
        {
            var comparer = MaliciousPackageGuard.GetComparer(FeedType.Npm);

            Assert.Equal(expected, MaliciousVersions.IsAffected(Stored, version, comparer));
        }

        [Fact]
        public void NullAffectsEveryVersion()
        {
            Assert.True(MaliciousVersions.IsAffected(null, "9.9.9", MaliciousPackageGuard.GetComparer(FeedType.Npm)));
        }

        [Fact]
        public void ComparesGoVersionsWithAndWithoutTheV()
        {
            const string stored = """{"ranges":[[{"introduced":"1.2.0"},{"fixed":"1.3.0"}]]}""";
            var comparer = MaliciousPackageGuard.GetComparer(FeedType.Go);

            Assert.True(MaliciousVersions.IsAffected(stored, "v1.2.5", comparer));
            Assert.False(MaliciousVersions.IsAffected(stored, "v1.10.0", comparer));
        }

        [Fact]
        public void ComparesNuGetVersionsNormalized()
        {
            const string stored = """{"versions":["1.0.0"]}""";

            Assert.True(MaliciousVersions.IsAffected(stored, "1.0", MaliciousPackageGuard.GetComparer(FeedType.NuGet)));
        }
    }
}
