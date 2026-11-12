using System.Collections.Generic;
using PaGetto.Core.PyPI;
using Xunit;

namespace PaGetto.Core.Tests.PyPI;

public class PyPIManifestSummaryTests
{
    public class Parse
    {
        [Fact]
        public void ReadsWhatThePackagePageShows()
        {
            var summary = PyPIManifestSummary.Parse(
                """
                {
                  "name": "My_Package",
                  "requires_python": ">=3.9",
                  "home_page": "https://example.com",
                  "project_url": ["Source Code, https://example.com/src", "Bug Tracker, https://example.com/issues", "Changelog, https://example.com/changes", "Evil, javascript:alert(1)"],
                  "requires_dist": ["requests[socks]>=2.0; python_version < \"3.12\"", "rich"],
                  "keywords": ["one", "two"],
                  "license_expression": "MIT OR Apache-2.0"
                }
                """);

            Assert.Equal("My_Package", summary.Name);
            Assert.Equal(">=3.9", summary.RequiresPython);
            Assert.Equal(
                new Dictionary<string, string>
                {
                    ["homepage"] = "https://example.com/",
                    ["repository"] = "https://example.com/src",
                    ["issue_tracker"] = "https://example.com/issues",
                    ["Changelog"] = "https://example.com/changes",
                },
                summary.Links);
            Assert.Equal(
                new[]
                {
                    KeyValuePair.Create("requests", "[socks]>=2.0; python_version < \"3.12\""),
                    KeyValuePair.Create("rich", "any version"),
                },
                summary.Dependencies);
            Assert.Equal(new[] { "one", "two" }, summary.Keywords);
            Assert.Equal("MIT OR Apache-2.0", summary.License);
        }

        [Theory]
        [InlineData("""{ "license": "BSD" }""", "BSD")]
        // A license field that holds the license text names no license; the classifier does.
        [InlineData("""{ "license": "Copyright (c)\nPermission is hereby granted", "classifier": ["License :: OSI Approved :: MIT License"] }""", "MIT License")]
        [InlineData("""{ }""", null)]
        public void ReadsTheLicenseName(string manifest, string expected)
        {
            Assert.Equal(expected, PyPIManifestSummary.Parse(manifest).License);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("[]")]
        public void AnythingElseGivesAnEmptySummary(string manifest)
        {
            var summary = PyPIManifestSummary.Parse(manifest);

            Assert.Null(summary.Name);
            Assert.Empty(summary.Links);
            Assert.Empty(summary.Dependencies);
        }
    }
}
