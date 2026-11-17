using System.Collections.Generic;
using System.Linq;
using PaGetto.Core.Cran;
using PaGetto.Core.Tests.Support;
using Xunit;

namespace PaGetto.Core.Tests.Cran;

public class CranManifestSummaryTests
{
    public class Parse
    {
        [Fact]
        public void ReadsWhatThePackagePageShows()
        {
            var description = CranPackageBuilder.Description("Hello", "1.0", "URL: https://hello.example.com, https://github.com/acme/hello\nBugReports: https://github.com/acme/hello/issues\nLinkingTo: Rcpp");
            var manifest = CranManifest.Create(CranDcf.ReadRecord(description), "0123");

            var summary = CranManifestSummary.Parse(manifest);

            Assert.Equal("Hello", summary.Name);
            Assert.Equal(">= 4.1.0", summary.RVersion);
            Assert.Equal(new[] { "rlang", "cli", "Rcpp" }, summary.Dependencies.Select(d => d.Key));
            Assert.Equal(new[] { ">= 1.0.0", "", "" }, summary.Dependencies.Select(d => d.Value));
            Assert.Equal("https://hello.example.com/", summary.Links["homepage"]);
            Assert.Equal("https://github.com/acme/hello/issues", summary.Links["issue_tracker"]);
            Assert.Equal("MIT + file LICENSE", summary.License);
            Assert.Equal("Ada Lovelace <ada@example.com>", summary.Maintainer);
            Assert.False(summary.NeedsCompilation);
        }

        [Fact]
        public void KeepsOnlyHttpLinks()
        {
            var manifest = CranManifest.Create([KeyValuePair.Create("URL", "javascript:alert(1)"), KeyValuePair.Create("BugReports", "mailto:a@example.com")], "0123");

            Assert.Empty(CranManifestSummary.Parse(manifest).Links);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("[1, 2]")]
        [InlineData("not json")]
        public void AnythingElseGivesAnEmptySummary(string manifest)
        {
            var summary = CranManifestSummary.Parse(manifest);

            Assert.Null(summary.Name);
            Assert.Empty(summary.Dependencies);
            Assert.Empty(summary.Links);
        }
    }
}
