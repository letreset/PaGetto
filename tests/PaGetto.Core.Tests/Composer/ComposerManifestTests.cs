using System;
using System.Text.Json.Nodes;
using PaGetto.Core.Composer;
using PaGetto.Core.Tests.Support;
using Xunit;

namespace PaGetto.Core.Tests.Composer;

public class ComposerManifestTests
{
    private const string Sha1 = "0123456789abcdef0123456789abcdef01234567";

    public class Create
    {
        [Fact]
        public void IsTheComposerJsonWithTheVersionAndADist()
        {
            Assert.True(ComposerVersion.TryParse("v1.2.0", out var version));
            var composerJson = (JsonObject)JsonNode.Parse(ComposerZipBuilder.ComposerJson("Acme/Hello"))!;

            var manifest = JsonNode.Parse(ComposerManifest.Create(composerJson, "acme/hello", version, new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc), Sha1))!;

            Assert.Equal("acme/hello", manifest["name"]!.GetValue<string>());
            Assert.Equal("v1.2.0", manifest["version"]!.GetValue<string>());
            Assert.Equal("1.2.0.0", manifest["version_normalized"]!.GetValue<string>());
            Assert.Equal("2020-01-02T03:04:05+00:00", manifest["time"]!.GetValue<string>());
            Assert.Equal("src/", manifest["autoload"]!["psr-4"]!["Acme\\Hello\\"]!.GetValue<string>());
            Assert.Equal("zip", manifest["dist"]!["type"]!.GetValue<string>());
            Assert.Equal(Sha1, manifest["dist"]!["reference"]!.GetValue<string>());
            Assert.Equal(Sha1, manifest["dist"]!["shasum"]!.GetValue<string>());
            Assert.Null(manifest["dist"]!["url"]);
        }
    }

    public class CreateMirrored
    {
        [Fact]
        public void KeepsTheReferenceOfTheMirror()
        {
            var upstream = (JsonObject)JsonNode.Parse("""
                {"name": "Acme/Hello", "version": "1.0.0", "source": {"type": "git", "reference": "abc"},
                 "dist": {"type": "zip", "url": "https://api.github.com/repos/acme/hello/zipball/abc", "reference": "abc", "shasum": ""}}
                """)!;

            var manifest = JsonNode.Parse(ComposerManifest.CreateMirrored(upstream, "acme/hello", Sha1))!;

            Assert.Equal("acme/hello", manifest["name"]!.GetValue<string>());
            Assert.Equal("abc", manifest["source"]!["reference"]!.GetValue<string>());
            Assert.Equal("abc", manifest["dist"]!["reference"]!.GetValue<string>());
            Assert.Equal(Sha1, manifest["dist"]!["shasum"]!.GetValue<string>());
            Assert.Null(manifest["dist"]!["url"]);
        }
    }

    public class ToPackage
    {
        [Fact]
        public void PointsTheDistAtTheFeed()
        {
            Assert.True(ComposerVersion.TryParse("1.0.0+build", out var version));
            var manifest = ComposerManifest.Create(new JsonObject(), "acme/hello", version, DateTime.UnixEpoch, Sha1);

            var package = ComposerManifest.ToPackage(manifest, "https://example.com/feeds/php/composer/dists", "acme/hello", "1.0.0+build");

            Assert.Equal($"https://example.com/feeds/php/composer/dists/acme/hello/1.0.0%2Bbuild/{Sha1}.zip", package!["dist"]!["url"]!.GetValue<string>());
        }

        [Fact]
        public void ManifestThatIsNotAnObjectIsNull()
        {
            Assert.Null(ComposerManifest.ToPackage("[]", "https://example.com", "acme/hello", "1.0.0"));
        }
    }
}
