using System.Collections.Generic;
using PaGetto.Core.Npm;
using Xunit;

namespace PaGetto.Core.Tests.Npm;

public class NpmManifestSummaryTests
{
    [Fact]
    public void ReadsTheFieldsShownOnThePackagePage()
    {
        var summary = NpmManifestSummary.Parse("""
            {
              "homepage": "https://example.com/docs",
              "repository": { "type": "git", "url": "git+https://github.com/example/thing.git" },
              "bugs": { "url": "https://github.com/example/thing/issues" },
              "engines": { "node": ">=18" },
              "dependencies": { "left-pad": "^1.3.0", "@scope/dep": "2.x" },
              "keywords": ["one", "", "two"],
              "license": "MIT",
              "deprecated": "Use another thing"
            }
            """);

        Assert.Equal("https://example.com/docs", summary.Links["homepage"]);
        Assert.Equal("https://github.com/example/thing", summary.Links["repository"]);
        Assert.Equal("https://github.com/example/thing/issues", summary.Links["bugs"]);
        Assert.Equal([KeyValuePair.Create("node", ">=18")], summary.Engines);
        Assert.Equal([KeyValuePair.Create("left-pad", "^1.3.0"), KeyValuePair.Create("@scope/dep", "2.x")], summary.Dependencies);
        Assert.Equal(["one", "two"], summary.Keywords);
        Assert.Equal("MIT", summary.License);
        Assert.Equal("Use another thing", summary.Deprecated);
    }

    [Theory]
    [InlineData("\"example/thing\"", "https://github.com/example/thing")]
    [InlineData("\"github:example/thing\"", "https://github.com/example/thing")]
    [InlineData("\"https://gitlab.com/example/thing\"", "https://gitlab.com/example/thing")]
    public void TurnsRepositoryFormsIntoWebUrls(string repository, string url)
    {
        var summary = NpmManifestSummary.Parse($"{{ \"repository\": {repository} }}");

        Assert.Equal(url, summary.Links["repository"]);
    }

    [Theory]
    [InlineData("{ \"homepage\": \"javascript:alert(1)\" }")]
    [InlineData("{ \"repository\": \"git@github.com:example/thing.git\" }")]
    public void LeavesOutLinksThatAreNotWebUrls(string manifest)
    {
        Assert.Empty(NpmManifestSummary.Parse(manifest).Links);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("[]")]
    public void GivesAnEmptySummaryForAnythingElse(string manifest)
    {
        var summary = NpmManifestSummary.Parse(manifest);

        Assert.Empty(summary.Links);
        Assert.Empty(summary.Dependencies);
        Assert.Null(summary.License);
    }

    [Fact]
    public void ReadsTheLicenseObjectOfOlderPackages()
    {
        Assert.Equal("BSD", NpmManifestSummary.Parse("{ \"license\": { \"type\": \"BSD\" } }").License);
    }
}
