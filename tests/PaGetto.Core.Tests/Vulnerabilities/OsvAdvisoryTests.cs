using System.Text.Json;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Entities;
using PaGetto.Core.Vulnerabilities;
using Xunit;

namespace PaGetto.Core.Tests.Vulnerabilities;

public class OsvAdvisoryTests
{
    // Shaped like a GitHub advisory on osv.dev.
    private const string Advisory = """
        {
          "id": "GHSA-35jh-r3h4-6jhm",
          "summary": "Command Injection in lodash",
          "aliases": ["CVE-2021-23337"],
          "severity": [{ "type": "CVSS_V3", "score": "CVSS:3.1/AV:N/AC:L/PR:H/UI:N/S:U/C:H/I:H/A:H" }],
          "affected": [
            {
              "package": { "ecosystem": "npm", "name": "lodash" },
              "ranges": [
                { "type": "SEMVER", "events": [{ "introduced": "0" }, { "fixed": "4.17.21" }] },
                { "type": "SEMVER", "events": [{ "introduced": "5.0.0" }, { "fixed": "5.0.2" }, { "introduced": "5.1.0" }] }
              ]
            },
            {
              "package": { "ecosystem": "npm", "name": "lodash-es" },
              "ranges": [{ "type": "SEMVER", "events": [{ "introduced": "0" }, { "fixed": "4.17.21" }] }]
            }
          ],
          "database_specific": { "severity": "HIGH" }
        }
        """;

    public class Describe
    {
        [Fact]
        public void DescribesTheFindingOfAVersion()
        {
            var finding = Parse(Advisory).Describe("npm", "lodash", "4.17.20", NpmComparer);

            Assert.Equal("GHSA-35jh-r3h4-6jhm", finding.AdvisoryId);
            Assert.Equal("CVE-2021-23337", finding.Aliases);
            Assert.Equal("Command Injection in lodash", finding.Summary);
            Assert.Equal(7.2, finding.CvssScore);
            Assert.Equal(VulnerabilitySeverity.High, finding.Severity);
            Assert.Equal("<4.17.21 || >=5.0.0 <5.0.2 || >=5.1.0", finding.AffectedVersions);
            Assert.Equal("4.17.21", finding.FixedVersion);
        }

        [Fact]
        public void PicksTheLowestFixedVersionAboveTheVersion()
        {
            var finding = Parse(Advisory).Describe("npm", "lodash", "5.0.1", NpmComparer);

            Assert.Equal("5.0.2", finding.FixedVersion);
        }

        [Fact]
        public void HasNoFixedVersionWhenNoneIsAboveTheVersion()
        {
            var finding = Parse(Advisory).Describe("npm", "lodash", "5.2.0", NpmComparer);

            Assert.Null(finding.FixedVersion);
        }

        [Fact]
        public void IgnoresOtherPackagesAndEcosystems()
        {
            Assert.Null(Parse(Advisory).Describe("npm", "underscore", "1.0.0", NpmComparer));
            Assert.Null(Parse(Advisory).Describe("PyPI", "lodash", "1.0.0", NpmComparer));
        }

        [Fact]
        public void UsesTheDatabaseSeverityWithoutACvssV3Vector()
        {
            var json = Advisory.Replace("CVSS_V3", "CVSS_V4", System.StringComparison.Ordinal).Replace("\"HIGH\"", "\"CRITICAL\"", System.StringComparison.Ordinal);

            var finding = Parse(json).Describe("npm", "lodash", "4.17.20", NpmComparer);

            Assert.Null(finding.CvssScore);
            Assert.Equal(VulnerabilitySeverity.Critical, finding.Severity);
        }

        [Fact]
        public void IsUnknownWithoutAnySeverity()
        {
            var json = """
                {
                  "id": "OSV-1",
                  "details": "First line.\nSecond line.",
                  "affected": [{ "package": { "ecosystem": "PyPI", "name": "Django_Thing" }, "versions": ["1.0", "1.1"] }]
                }
                """;

            var finding = Parse(json).Describe("PyPI", "django-thing", "1.0", ArtifactVersionOrder.GetComparer(FeedType.PyPI));

            Assert.Equal(VulnerabilitySeverity.Unknown, finding.Severity);
            Assert.Equal("First line.", finding.Summary);
            Assert.Equal("1.0 || 1.1", finding.AffectedVersions);
            Assert.Null(finding.Aliases);
        }

        [Fact]
        public void MatchesEcosystemsWithASuffix()
        {
            var json = Advisory.Replace("\"ecosystem\": \"npm\"", "\"ecosystem\": \"Maven:central\"", System.StringComparison.Ordinal)
                .Replace("\"lodash\"", "\"org.example:lib\"", System.StringComparison.Ordinal);

            Assert.NotNull(Parse(json).Describe("Maven", "org.example:lib", "1.0", ArtifactVersionOrder.GetComparer(FeedType.Maven)));
        }

        [Fact]
        public void AWithdrawnAdvisoryAffectsNothing()
        {
            var json = Advisory.Replace("\"id\":", "\"withdrawn\": \"2022-01-01T00:00:00Z\", \"id\":", System.StringComparison.Ordinal);

            Assert.Null(Parse(json).Describe("npm", "lodash", "4.17.20", NpmComparer));
        }

        [Fact]
        public void DescribesLastAffectedVersions()
        {
            var json = """
                {
                  "id": "RUSTSEC-1",
                  "affected": [{
                    "package": { "ecosystem": "crates.io", "name": "smallvec" },
                    "ranges": [{ "type": "SEMVER", "events": [{ "introduced": "0.6.3" }, { "last_affected": "0.6.9" }] }]
                  }]
                }
                """;

            var finding = Parse(json).Describe("crates.io", "smallvec", "0.6.5", ArtifactVersionOrder.GetComparer(FeedType.Cargo));

            Assert.Equal(">=0.6.3 <=0.6.9", finding.AffectedVersions);
            Assert.Null(finding.FixedVersion);
        }
    }

    private static System.Collections.Generic.IComparer<string> NpmComparer => ArtifactVersionOrder.GetComparer(FeedType.Npm);

    private static OsvAdvisory Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return OsvAdvisory.Parse(document.RootElement);
    }
}
