using System.Collections.Generic;
using PaGetto.Core.Composer;
using Xunit;

namespace PaGetto.Core.Tests.Composer;

public class ComposerManifestSummaryTests
{
    public class Parse
    {
        [Fact]
        public void ReadsWhatThePackagePageShows()
        {
            var summary = ComposerManifestSummary.Parse("""
                {"type": "library", "license": ["MIT", "Apache-2.0"], "keywords": ["log", "psr-3"],
                 "homepage": "https://example.com", "require": {"php": ">=8.1", "ext-json": "*", "psr/log": "^3.0"},
                 "support": {"source": "https://github.com/acme/hello", "issues": "javascript:alert(1)", "docs": "https://docs.example.com"}}
                """);

            Assert.Equal("library", summary.Type);
            Assert.Equal(">=8.1", summary.Php);
            Assert.Equal([KeyValuePair.Create("psr/log", "^3.0")], summary.Dependencies);
            Assert.Equal("MIT, Apache-2.0", summary.License);
            Assert.Equal(["log", "psr-3"], summary.Keywords);
            Assert.Equal("https://example.com/", summary.Links["homepage"]);
            Assert.Equal("https://github.com/acme/hello", summary.Links["repository"]);
            Assert.Equal("https://docs.example.com/", summary.Links["documentation"]);
            Assert.False(summary.Links.ContainsKey("issue_tracker"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("""{"license": 1, "require": [], "keywords": "log"}""")]
        public void UnexpectedManifestGivesAnEmptySummary(string manifestJson)
        {
            var summary = ComposerManifestSummary.Parse(manifestJson);

            Assert.Null(summary.License);
            Assert.Null(summary.Php);
            Assert.Empty(summary.Dependencies);
            Assert.Empty(summary.Keywords);
            Assert.Empty(summary.Links);
        }
    }
}
