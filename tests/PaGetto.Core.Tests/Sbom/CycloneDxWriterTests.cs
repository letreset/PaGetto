using System;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using CycloneDX;
using PaGetto.Core.Entities;
using PaGetto.Core.Sbom;
using Xunit;

namespace PaGetto.Core.Tests.Sbom;

public class CycloneDxWriterTests
{
    public class Write
    {
        private static readonly Guid _serialNumber = Guid.Parse("3e671687-395b-41f5-a30f-a58921a69b79");
        private static readonly DateTime _timestamp = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static TheoryData<FeedType> FeedTypes => new(Enum.GetValues<FeedType>());

        [Theory]
        [MemberData(nameof(FeedTypes))]
        public void WritesADocumentTheSchemaAcceptsForEveryFeedType(FeedType feedType)
        {
            var document = WriteSample(feedType, Vulnerability());

            var result = CycloneDX.Json.Validator.Validate(Encoding.UTF8.GetString(document), SpecificationVersion.v1_6);

            Assert.True(result.Valid, string.Join(Environment.NewLine, result.Messages ?? []));
        }

        [Fact]
        public void DescribesTheVersion()
        {
            var bom = Parse(WriteSample(FeedType.Npm));

            Assert.Equal("CycloneDX", (string)bom["bomFormat"]);
            Assert.Equal("1.6", (string)bom["specVersion"]);
            Assert.Equal("urn:uuid:3e671687-395b-41f5-a30f-a58921a69b79", (string)bom["serialNumber"]);
            Assert.Equal("2020-01-01T00:00:00Z", (string)bom["metadata"]["timestamp"]);
            Assert.Equal("PaGetto", (string)bom["metadata"]["tools"]["components"][0]["name"]);

            var component = bom["metadata"]["component"];
            Assert.Equal("library", (string)component["type"]);
            Assert.Equal("@scope", (string)component["group"]);
            Assert.Equal("thing", (string)component["name"]);
            Assert.Equal("1.0.0", (string)component["version"]);
            Assert.Equal("The Npm sample.", (string)component["description"]);
            Assert.Equal("pkg:npm/%40scope/thing@1.0.0", (string)component["purl"]);
            Assert.Equal("pkg:npm/%40scope/thing@1.0.0", (string)component["bom-ref"]);
            Assert.Equal("MIT", (string)component["licenses"][0]["expression"]);
            Assert.Equal("SHA-256", (string)component["hashes"][0]["alg"]);
            Assert.Equal(SbomSamples.Sha256, (string)component["hashes"][0]["content"]);
            Assert.Null(bom["vulnerabilities"]);
        }

        [Fact]
        public void ListsTheDeclaredDependencies()
        {
            var bom = Parse(WriteSample(FeedType.Npm));

            var components = bom["components"].AsArray();
            Assert.Equal(["pkg:npm/left-pad", "pkg:npm/%40scope/dep"], components.Select(c => (string)c["purl"]));
            Assert.Equal("left-pad", (string)components[0]["name"]);
            Assert.Null(components[0]["version"]);
            Assert.Equal(CycloneDxWriter.RequirementProperty, (string)components[0]["properties"][0]["name"]);
            Assert.Equal("^1.3.0", (string)components[0]["properties"][0]["value"]);

            var dependencies = Assert.Single(bom["dependencies"].AsArray());
            Assert.Equal("pkg:npm/%40scope/thing@1.0.0", (string)dependencies["ref"]);
            Assert.Equal(["pkg:npm/left-pad", "pkg:npm/%40scope/dep"], dependencies["dependsOn"].AsArray().Select(d => (string)d));
        }

        [Fact]
        public void ListsAPackageDeclaredForSeveralFrameworksOnce()
        {
            var bom = Parse(WriteSample(FeedType.NuGet));

            var component = Assert.Single(bom["components"].AsArray());
            Assert.Equal("pkg:nuget/Newtonsoft.Json", (string)component["purl"]);
            Assert.Equal(
                ["[13.0.1, ) (net8.0)", "[12.0.1, ) (netstandard2.0)"],
                component["properties"].AsArray().Select(p => (string)p["value"]));
        }

        [Fact]
        public void NamesALicenseThatIsNoExpression()
        {
            var bom = Parse(WriteSample(FeedType.Maven));

            var license = Assert.Single(bom["metadata"]["component"]["licenses"].AsArray());
            Assert.Equal("Apache License, Version 2.0", (string)license["license"]["name"]);
            Assert.Null(license["expression"]);
        }

        [Fact]
        public void ListsTheFilesOfAVersionWithTheirChecksums()
        {
            var bom = Parse(WriteSample(FeedType.Maven));

            var component = bom["metadata"]["component"];
            Assert.Equal("com.example", (string)component["group"]);
            Assert.Equal("my-lib", (string)component["name"]);
            Assert.Null(component["hashes"]);

            var files = component["components"].AsArray();
            Assert.Equal(["my-lib-1.0.pom", "my-lib-1.0.jar"], files.Select(f => (string)f["name"]));
            Assert.All(files, f => Assert.Equal("file", (string)f["type"]));
            Assert.Equal(["MD5", "SHA-1", "SHA-256", "SHA-512"], files[0]["hashes"].AsArray().Select(h => (string)h["alg"]));
        }

        [Fact]
        public void AddsTheKnownVulnerabilities()
        {
            var bom = Parse(WriteSample(FeedType.Npm, Vulnerability()));

            var vulnerability = Assert.Single(bom["vulnerabilities"].AsArray());
            Assert.Equal("GHSA-35jh-r3h4-6jhm", (string)vulnerability["id"]);
            Assert.Equal("https://osv.dev/vulnerability/GHSA-35jh-r3h4-6jhm", (string)vulnerability["source"]["url"]);
            Assert.Equal(["CVE-2021-23337", "CVE-2021-23338"], vulnerability["references"].AsArray().Select(r => (string)r["id"]));
            Assert.Equal("high", (string)vulnerability["ratings"][0]["severity"]);
            Assert.Equal(7.2, (double)vulnerability["ratings"][0]["score"]);
            Assert.Equal("CVSSv3", (string)vulnerability["ratings"][0]["method"]);
            Assert.Equal("Command injection.", (string)vulnerability["description"]);
            Assert.Equal("Upgrade to 1.0.1.", (string)vulnerability["recommendation"]);
            Assert.Equal("pkg:npm/%40scope/thing@1.0.0", (string)vulnerability["affects"][0]["ref"]);
            Assert.Equal("1.0.0", (string)vulnerability["affects"][0]["versions"][0]["version"]);
        }

        [Fact]
        public void RatesAVulnerabilityWithoutAScoreBySeverityOnly()
        {
            var vulnerability = Vulnerability();
            vulnerability.CvssScore = null;
            vulnerability.Severity = VulnerabilitySeverity.Moderate;

            var rating = Parse(WriteSample(FeedType.Npm, vulnerability))["vulnerabilities"][0]["ratings"][0];

            Assert.Equal("medium", (string)rating["severity"]);
            Assert.Null(rating["score"]);
            Assert.Null(rating["method"]);
        }

        [Fact]
        public void RefersToAnExtensionByNameAndVersion()
        {
            var bom = Parse(WriteSample(FeedType.Vsix));

            var component = bom["metadata"]["component"];
            Assert.Equal("Acme.Hello@1.0.0", (string)component["bom-ref"]);
            Assert.Null(component["purl"]);
            Assert.Equal("Acme.Base", (string)bom["components"][0]["bom-ref"]);
        }

        private static byte[] WriteSample(FeedType feedType, params PackageVulnerability[] vulnerabilities)
        {
            var package = SbomSamples.Read(SbomSamples.Get(feedType));

            return CycloneDxWriter.Write(package, vulnerabilities, _serialNumber, _timestamp, "1.0.0");
        }

        private static JsonNode Parse(byte[] document)
        {
            return JsonNode.Parse(document);
        }

        private static PackageVulnerability Vulnerability()
        {
            return new PackageVulnerability
            {
                AdvisoryId = "GHSA-35jh-r3h4-6jhm",
                Aliases = "CVE-2021-23337, CVE-2021-23338",
                Summary = "Command injection.",
                Severity = VulnerabilitySeverity.High,
                CvssScore = 7.2,
                PackageVersion = "1.0.0",
                AffectedVersions = "<1.0.1",
                FixedVersion = "1.0.1",
            };
        }
    }
}
