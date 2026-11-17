using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using PaGetto.Core.Swift;
using Xunit;

namespace PaGetto.Core.Tests.Swift;

public class SwiftManifestTests
{
    private static readonly Dictionary<string, string> _manifests = new()
    {
        ["Package.swift"] = "// swift-tools-version:5.9\nimport PackageDescription\n",
        ["Package@swift-5.7.swift"] = "// swift-tools-version:5.7\n",
        ["Package@swift-5.swift"] = "// swift-tools-version: 5.3.1\n",
        ["Package@swift-6.swift"] = "no tools version\n",
    };

    public class ToJson
    {
        [Fact]
        public void RoundTripsThroughParse()
        {
            var metadata = new JsonObject { ["description"] = "A linked list" };
            var json = SwiftManifest.Create("mona.LinkedList", "1.0.0", metadata, "c2ln", "cms-1.0.0", new DateTime(2020, 1, 1, 12, 30, 15, DateTimeKind.Utc), _manifests).ToJson();

            var manifest = SwiftManifest.Parse(json);

            Assert.Equal("mona.LinkedList", manifest.Id);
            Assert.Equal("1.0.0", manifest.Version);
            Assert.Equal("A linked list", manifest.Metadata["description"]!.GetValue<string>());
            Assert.Equal("c2ln", manifest.Signature);
            Assert.Equal("cms-1.0.0", manifest.SignatureFormat);
            Assert.Equal("2020-01-01T12:30:15Z", manifest.PublishedAt);
            Assert.Equal(_manifests, manifest.Manifests);
        }
    }

    public class Parse
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("[]")]
        public void AnythingElseGivesAnEmptyManifest(string json)
        {
            var manifest = SwiftManifest.Parse(json);

            Assert.Null(manifest.Id);
            Assert.Empty(manifest.Metadata);
            Assert.Empty(manifest.Manifests);
        }
    }

    public class ToReleaseJson
    {
        [Fact]
        public void WritesTheReleaseMetadataOfTheRegistryApi()
        {
            var manifest = SwiftManifest.Create("mona.LinkedList", "1.0.0", new JsonObject { ["description"] = "A list" }, null, null, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), _manifests);

            Assert.Equal(
                """{"id":"mona.LinkedList","version":"1.0.0","resources":[{"name":"source-archive","type":"application/zip","checksum":"abc"}],"metadata":{"description":"A list"},"publishedAt":"2020-01-01T00:00:00Z"}""",
                manifest.ToReleaseJson("abc"));
        }

        [Fact]
        public void IncludesTheSignatureOfASignedArchive()
        {
            var manifest = SwiftManifest.Create("mona.LinkedList", "1.0.0", null, "c2ln", "cms-1.0.0", DateTime.UtcNow, _manifests);

            var resource = JsonNode.Parse(manifest.ToReleaseJson("abc"))!["resources"]![0]!;

            Assert.Equal("""{"signatureBase64Encoded":"c2ln","signatureFormat":"cms-1.0.0"}""", resource["signing"]!.ToJsonString());
        }
    }

    public class GetAlternateManifests
    {
        [Fact]
        public void ListsTheVersionSpecificManifestsWithATargetVersion()
        {
            var manifest = SwiftManifest.Create("mona.LinkedList", "1.0.0", null, null, null, DateTime.UtcNow, _manifests);

            Assert.Equal(
                new[] { ("Package@swift-5.7.swift", "5.7", "5.7"), ("Package@swift-5.swift", "5", "5.3.1") },
                manifest.GetAlternateManifests());
        }
    }

    public class FindVersionSpecificManifest
    {
        [Theory]
        [InlineData("5.7", "Package@swift-5.7.swift")]
        [InlineData("5.7.0", "Package@swift-5.7.swift")]
        [InlineData("5", "Package@swift-5.swift")]
        [InlineData("5.0.0", "Package@swift-5.swift")]
        // Swift Package Manager asks with the tools version an alternate declares.
        [InlineData("5.3.1", "Package@swift-5.swift")]
        [InlineData("5.8", null)]
        [InlineData("6", null)]
        [InlineData("latest", null)]
        public void FindsTheManifestOfASwiftVersion(string swiftVersion, string fileName)
        {
            var manifest = SwiftManifest.Create("mona.LinkedList", "1.0.0", null, null, null, DateTime.UtcNow, _manifests);

            Assert.Equal(fileName, manifest.FindVersionSpecificManifest(swiftVersion));
        }
    }

    public class GetToolsVersion
    {
        [Theory]
        [InlineData("// swift-tools-version:5.9\nimport PackageDescription", "5.9")]
        [InlineData("// swift-tools-version: 5.10.1", "5.10.1")]
        [InlineData("//swift-tools-version:6.0;(experimentalCxxInterop)", "6.0")]
        [InlineData("// Swift-Tools-Version:4.2\r\n", "4.2")]
        [InlineData("import PackageDescription\n// swift-tools-version:5.9", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void ReadsTheFirstLine(string manifest, string toolsVersion)
        {
            Assert.Equal(toolsVersion, SwiftManifest.GetToolsVersion(manifest));
        }
    }

    public class IsManifestFileName
    {
        [Theory]
        [InlineData("Package.swift", true)]
        [InlineData("Package@swift-5.swift", true)]
        [InlineData("Package@swift-5.7.1.swift", true)]
        [InlineData("package.swift", false)]
        [InlineData("Package@swift-.swift", false)]
        [InlineData("Package@swift-5.7.1.0.swift", false)]
        [InlineData("Sources/Package.swift", false)]
        public void MatchesTheManifestsOfThePackageFolder(string fileName, bool isManifest)
        {
            Assert.Equal(isManifest, SwiftManifest.IsManifestFileName(fileName));
        }
    }
}
