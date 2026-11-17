using System.Collections.Generic;
using PaGetto.Core.CocoaPods;
using Xunit;

namespace PaGetto.Core.Tests.CocoaPods;

public class CocoaPodsManifestSummaryTests
{
    public class Parse
    {
        [Fact]
        public void ReadsWhatThePackagePageShows()
        {
            var summary = CocoaPodsManifestSummary.Parse("""
                {
                  "name": "MyLib",
                  "version": "1.0.0",
                  "homepage": "https://example.com/mylib",
                  "documentation_url": "javascript:alert(1)",
                  "license": {"type": "MIT", "file": "LICENSE"},
                  "platforms": {"ios": "12.0", "osx": "10.13", "tvos": null},
                  "swift_versions": ["5.0", "5.9"],
                  "source": {"git": "https://github.com/acme/mylib.git", "tag": "1.0.0"},
                  "dependencies": {"Alamofire": ["~> 5.0"], "SwiftyJSON": []}
                }
                """);

            Assert.Equal("MyLib", summary.Name);
            Assert.Equal(
                new[] { KeyValuePair.Create("iOS", "12.0"), KeyValuePair.Create("macOS", "10.13"), KeyValuePair.Create("tvOS", "any"), KeyValuePair.Create("Swift", "5.0, 5.9") },
                summary.Platforms);
            Assert.Equal(new[] { KeyValuePair.Create("Alamofire", "~> 5.0"), KeyValuePair.Create("SwiftyJSON", "") }, summary.Dependencies);
            Assert.Equal("https://example.com/mylib", summary.Links["homepage"]);
            Assert.Equal("https://github.com/acme/mylib.git", summary.Links["repository"]);
            Assert.False(summary.Links.ContainsKey("documentation"));
            Assert.Equal("MIT", summary.License);
        }

        [Fact]
        public void ReadsALicenseThatIsAString()
        {
            Assert.Equal("Apache-2.0", CocoaPodsManifestSummary.Parse("""{"license": "Apache-2.0", "swift_version": "5.0"}""").License);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("[]")]
        public void AnythingElseGivesAnEmptySummary(string json)
        {
            var summary = CocoaPodsManifestSummary.Parse(json);

            Assert.Null(summary.Name);
            Assert.Empty(summary.Platforms);
            Assert.Empty(summary.Dependencies);
        }
    }
}
