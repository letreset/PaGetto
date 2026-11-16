using PaGetto.Core.Hex;
using Xunit;

namespace PaGetto.Core.Tests.Hex;

public class HexManifestTests
{
    public class FromMetadata
    {
        [Fact]
        public void KeepsOnlyHttpLinks()
        {
            var manifest = HexManifest.FromMetadata(
                [new object[] { "links", new System.Collections.Generic.List<object> { new object[] { "Docs", "https://example.com" }, new object[] { "Bad", "javascript:alert(1)" } } }],
                innerChecksum: null);

            Assert.Equal("https://example.com", Assert.Single(manifest.Links).Value);
        }

        [Fact]
        public void RejectsDuplicateKeys()
        {
            Assert.Throws<InvalidHexPackageException>(() => HexManifest.FromMetadata(
                [new object[] { "name", "a" }, new object[] { "name", "b" }],
                innerChecksum: null));
        }

        [Fact]
        public void RejectsRequirementsWithoutAVersionRequirement()
        {
            Assert.Throws<InvalidHexPackageException>(() => HexManifest.FromMetadata(
                [new object[] { "requirements", new System.Collections.Generic.List<object> { new object[] { "jason", new System.Collections.Generic.List<object>() } } }],
                innerChecksum: null));
        }
    }

    public class Parse
    {
        [Fact]
        public void ReadsWhatToJsonWrites()
        {
            var manifest = new HexManifest
            {
                Name = "demo",
                Version = "1.0.0",
                Requirements = [new HexRequirement { Name = "jason", Requirement = "~> 1.4", Repository = "hexpm" }],
                InnerChecksum = "ab",
            }.WithRetirement(new HexRetirement { Reason = "deprecated", Message = "Old" });

            var parsed = HexManifest.Parse(manifest.ToJson());

            Assert.Equal("demo", parsed.Name);
            Assert.Equal("~> 1.4", Assert.Single(parsed.Requirements).Requirement);
            Assert.Equal("ab", parsed.InnerChecksum);
            Assert.Equal("deprecated", parsed.Retirement.Reason);
            Assert.Equal("Old", parsed.Retirement.Message);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("[]")]
        public void ReadsAnythingElseAsAnEmptyManifest(string json)
        {
            var manifest = HexManifest.Parse(json);

            Assert.Null(manifest.Name);
            Assert.Empty(manifest.Requirements);
        }
    }
}
