using System.Collections.Generic;
using PaGetto.Core.Cargo;
using Xunit;

namespace PaGetto.Core.Tests.Cargo;

public class CargoManifestSummaryTests
{
    public class Parse
    {
        [Fact]
        public void ReadsTheFieldsThePackagePageShows()
        {
            const string manifest = """
                {
                  "name": "My_Crate", "vers": "1.0.0", "rust_version": "1.70", "license": "MIT OR Apache-2.0",
                  "homepage": "https://example.com", "repository": "javascript:alert(1)", "documentation": "https://docs.example.com/",
                  "keywords": ["parser", ""],
                  "deps": [
                    { "name": "serde", "req": "^1.0", "kind": "normal" },
                    { "name": "renamed", "package": "other_crate", "req": "=2.0.0", "kind": "build" },
                    { "name": "criterion", "req": "^0.5", "kind": "dev" }
                  ]
                }
                """;

            var summary = CargoManifestSummary.Parse(manifest);

            Assert.Equal("My_Crate", summary.Name);
            Assert.Equal("1.70", summary.RustVersion);
            Assert.Equal("MIT OR Apache-2.0", summary.License);
            Assert.Equal(new[] { "parser" }, summary.Keywords);

            // Only http and https links are kept.
            Assert.Equal(
                new Dictionary<string, string> { ["homepage"] = "https://example.com/", ["documentation"] = "https://docs.example.com/" },
                summary.Links);

            // Development dependencies are left out; a renamed one is listed under its crate.
            Assert.Equal(
                new[] { KeyValuePair.Create("serde", "^1.0"), KeyValuePair.Create("other_crate", "=2.0.0") },
                summary.Dependencies);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("[]")]
        public void GivesAnEmptySummaryForAnythingElse(string manifest)
        {
            var summary = CargoManifestSummary.Parse(manifest);

            Assert.Null(summary.Name);
            Assert.Empty(summary.Dependencies);
            Assert.Empty(summary.Links);
        }
    }
}
