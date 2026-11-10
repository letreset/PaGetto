using System.Linq;
using PaGetto.Core.Pub;
using Xunit;

namespace PaGetto.Core.Tests.Pub;

public class PubspecSummaryTests
{
    public class Parse
    {
        [Fact]
        public void ReadsLinksConstraintsDependenciesAndTopics()
        {
            var summary = PubspecSummary.Parse("""
                {
                  "name": "my_package",
                  "homepage": "https://example.com",
                  "repository": "https://github.com/example/my_package",
                  "environment": { "sdk": "^3.0.0", "flutter": ">=3.10.0" },
                  "dependencies": {
                    "http": "^1.0.0",
                    "anything": null,
                    "flutter": { "sdk": "flutter" },
                    "hosted_one": { "hosted": "https://pub.example.com", "version": "^2.0.0" },
                    "from_git": { "git": "https://github.com/example/x.git" }
                  },
                  "topics": ["network", "http"]
                }
                """);

            Assert.Equal("https://example.com/", summary.Links["homepage"]);
            Assert.Equal("https://github.com/example/my_package", summary.Links["repository"]);
            Assert.Equal(new[] { "sdk ^3.0.0", "flutter >=3.10.0" }, summary.Environment.Select(e => $"{e.Key} {e.Value}"));
            Assert.Equal(
                new[] { "http ^1.0.0", "anything any", "flutter SDK flutter", "hosted_one ^2.0.0", "from_git git" },
                summary.Dependencies.Select(d => $"{d.Key} {d.Value}"));
            Assert.Equal(new[] { "network", "http" }, summary.Topics);
        }

        [Theory]
        [InlineData("javascript:alert(1)")]
        [InlineData("/relative")]
        [InlineData("ftp://example.com")]
        public void DropsLinksThatAreNotWebUrls(string url)
        {
            var summary = PubspecSummary.Parse($$"""{ "homepage": "{{url}}" }""");

            Assert.Empty(summary.Links);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("[1, 2]")]
        [InlineData("""{ "dependencies": "text", "environment": 3, "topics": {} }""")]
        public void GivesAnEmptySummaryForAnythingUnexpected(string json)
        {
            var summary = PubspecSummary.Parse(json);

            Assert.Empty(summary.Links);
            Assert.Empty(summary.Environment);
            Assert.Empty(summary.Dependencies);
            Assert.Empty(summary.Topics);
        }
    }
}
