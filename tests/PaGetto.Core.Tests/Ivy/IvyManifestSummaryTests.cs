using System.Collections.Generic;
using PaGetto.Core.Ivy;
using Xunit;

namespace PaGetto.Core.Tests.Ivy;

public class IvyManifestSummaryTests
{
    public class Parse
    {
        [Fact]
        public void ReadsTheNameLinksLicenseAndDependencies()
        {
            var summary = IvyManifestSummary.Parse("""
                {
                  "organisation": "com.Example", "module": "sbt-tool", "revision": "1.0.0",
                  "scalaVersion": "2.12", "sbtVersion": "1.0", "status": "release",
                  "homepage": "https://example.com/sbt-tool", "licenses": ["Apache-2.0", "MIT"],
                  "dependencies": [
                    { "org": "org.scala-lang", "name": "scala-library", "rev": "2.12.20", "conf": "provided->default(compile)" },
                    { "org": "com.example", "name": "sbt-other", "rev": "0.5.0", "scalaVersion": "2.12", "sbtVersion": "1.0" },
                    { "name": "no-org" }
                  ]
                }
                """);

            Assert.Equal("com.Example:sbt-tool:scala_2.12:sbt_1.0", summary.Name);
            Assert.True(summary.IsSbtPlugin);
            Assert.Equal("release", summary.Status);
            Assert.Equal("https://example.com/sbt-tool", summary.Links["homepage"]);
            Assert.Equal("Apache-2.0, MIT", summary.License);
            Assert.Equal(
                new[]
                {
                    KeyValuePair.Create("org.scala-lang:scala-library", "2.12.20 · provided->default(compile)"),
                    KeyValuePair.Create("com.example:sbt-other:scala_2.12:sbt_1.0", "0.5.0"),
                },
                summary.Dependencies);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("[]")]
        public void GivesAnEmptySummaryForSomethingElse(string json)
        {
            var summary = IvyManifestSummary.Parse(json);

            Assert.Null(summary.Name);
            Assert.False(summary.IsSbtPlugin);
            Assert.Empty(summary.Dependencies);
        }

        [Fact]
        public void LeavesOutLinksThatAreNotHttp()
        {
            var summary = IvyManifestSummary.Parse("""{ "organisation": "a", "module": "b", "homepage": "javascript:alert(1)" }""");

            Assert.Empty(summary.Links);
            Assert.False(summary.IsSbtPlugin);
            Assert.Equal("a:b", summary.Name);
        }
    }
}
