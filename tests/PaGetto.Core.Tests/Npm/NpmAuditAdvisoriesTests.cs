using System.Collections.Generic;
using PaGetto.Core.Entities;
using PaGetto.Core.Npm;
using Xunit;

namespace PaGetto.Core.Tests.Npm;

public class NpmAuditAdvisoriesTests
{
    public class Create
    {
        [Fact]
        public void ListsEachAdvisoryOnceUnderTheRequestedName()
        {
            var requested = new Dictionary<string, string> { ["lodash"] = "Lodash" };
            var findings = new[]
            {
                Finding("lodash", "4.17.20", "GHSA-1", VulnerabilitySeverity.Critical),
                Finding("lodash", "4.17.19", "GHSA-1", VulnerabilitySeverity.Critical),
                Finding("lodash", "4.17.19", "GHSA-2", VulnerabilitySeverity.Unknown),
                Finding("other", "1.0.0", "GHSA-3", VulnerabilitySeverity.Low),
            };

            var result = NpmAuditAdvisories.Create(requested, findings);

            var advisories = Assert.Single(result).Value!.AsArray();
            Assert.Equal("Lodash", Assert.Single(result).Key);
            Assert.Equal(2, advisories.Count);

            var first = advisories[0]!;
            Assert.Equal(NpmAuditAdvisories.GetNumericId("GHSA-1"), first["id"]!.GetValue<int>());
            Assert.Equal("https://osv.dev/vulnerability/GHSA-1", first["url"]!.GetValue<string>());
            Assert.Equal("critical", first["severity"]!.GetValue<string>());
            Assert.Equal("<4.17.21", first["vulnerable_versions"]!.GetValue<string>());
            Assert.Equal("info", advisories[1]!["severity"]!.GetValue<string>());
        }

        [Fact]
        public void NumericIdsAreStableAndPositive()
        {
            Assert.Equal(NpmAuditAdvisories.GetNumericId("GHSA-1"), NpmAuditAdvisories.GetNumericId("GHSA-1"));
            Assert.NotEqual(NpmAuditAdvisories.GetNumericId("GHSA-1"), NpmAuditAdvisories.GetNumericId("GHSA-2"));
            Assert.True(NpmAuditAdvisories.GetNumericId("GHSA-1") >= 0);
        }

        private static PackageVulnerability Finding(string name, string version, string advisory, VulnerabilitySeverity severity)
        {
            return new PackageVulnerability
            {
                PackageName = name,
                PackageVersion = version,
                AdvisoryId = advisory,
                Severity = severity,
                AffectedVersions = "<4.17.21",
            };
        }
    }
}
