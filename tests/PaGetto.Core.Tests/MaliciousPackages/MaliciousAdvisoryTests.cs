using System;
using System.Text.Json;
using PaGetto.Core.MaliciousPackages;
using Xunit;

namespace PaGetto.Core.Tests.MaliciousPackages;

public class MaliciousAdvisoryTests
{
    public class ParseOrNull
    {
        [Fact]
        public void ReadsAnAdvisoryOfTheMaliciousPackagesData()
        {
            using var json = JsonDocument.Parse("""
                {
                  "id": "MAL-2026-20205",
                  "modified": "2026-11-19T07:30:05.842365090Z",
                  "summary": "Malicious code in nectarine4-notthedevs (npm)",
                  "affected": [{
                    "package": { "name": "nectarine4-notthedevs", "ecosystem": "npm" },
                    "ranges": [{ "type": "SEMVER", "events": [{ "introduced": "0" }] }]
                  }]
                }
                """);

            var advisory = MaliciousAdvisory.ParseOrNull(json.RootElement);

            Assert.NotNull(advisory);
            Assert.Equal("MAL-2026-20205", advisory.Id);
            Assert.Equal(new DateTime(2026, 11, 19, 7, 30, 5, DateTimeKind.Utc).AddTicks(8423650), advisory.ModifiedUtc);
            Assert.Equal("Malicious code in nectarine4-notthedevs (npm)", advisory.Summary);
            Assert.False(advisory.Withdrawn);
            var package = Assert.Single(advisory.Packages);
            Assert.Equal(("npm", "nectarine4-notthedevs", (string)null), package);
        }

        [Fact]
        public void IgnoresOtherAdvisories()
        {
            using var json = JsonDocument.Parse("""{ "id": "GHSA-35jh-r3h4-6jhm" }""");

            Assert.Null(MaliciousAdvisory.ParseOrNull(json.RootElement));
        }

        [Fact]
        public void ReadsWithdrawnAdvisories()
        {
            using var json = JsonDocument.Parse("""{ "id": "MAL-1", "withdrawn": "2024-01-01T00:00:00Z" }""");

            Assert.True(MaliciousAdvisory.ParseOrNull(json.RootElement)!.Withdrawn);
        }
    }

    public class TryParseTimestamp
    {
        [Theory]
        [InlineData("2026-11-19T07:30:05.84236509Z")]
        [InlineData("2026-11-19T07:30:05.842365Z")]
        [InlineData("2026-11-19T07:30:05.8423650Z")]
        public void ReadsUpToNineFractionalDigits(string text)
        {
            Assert.True(MaliciousAdvisory.TryParseTimestamp(text, out var utc));
            Assert.Equal(DateTimeKind.Utc, utc.Kind);
            Assert.Equal(new DateTime(2026, 11, 19, 7, 30, 5, DateTimeKind.Utc).AddTicks(8423650), utc);
        }
    }
}
