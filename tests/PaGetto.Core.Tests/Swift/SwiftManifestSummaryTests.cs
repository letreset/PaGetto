using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using PaGetto.Core.Swift;
using Xunit;

namespace PaGetto.Core.Tests.Swift;

public class SwiftManifestSummaryTests
{
    public class Parse
    {
        [Fact]
        public void ReadsWhatThePackagePageShows()
        {
            var metadata = (JsonObject)JsonNode.Parse("""
                {
                  "licenseURL": "https://example.com/LICENSE",
                  "repositoryURLs": ["git@github.com:mona/LinkedList.git", "https://github.com/mona/LinkedList"],
                  "author": {"name": "Mona"}
                }
                """)!;
            var json = SwiftManifest.Create(
                "mona.LinkedList", "1.0.0", metadata, "c2ln", "cms-1.0.0", DateTime.UtcNow,
                new Dictionary<string, string> { ["Package.swift"] = "// swift-tools-version:5.9\n" }).ToJson();

            var summary = SwiftManifestSummary.Parse(json);

            Assert.Equal("mona.LinkedList", summary.Id);
            Assert.Equal("5.9", summary.ToolsVersion);
            Assert.Equal("Mona", summary.Author);
            Assert.True(summary.IsSigned);
            Assert.Equal("https://github.com/mona/LinkedList", summary.Links["repository"]);
            Assert.Equal("https://example.com/LICENSE", summary.Links["license"]);
        }

        [Fact]
        public void LeavesOutLinksThatAreNotWebUrls()
        {
            var metadata = new JsonObject { ["licenseURL"] = "javascript:alert(1)", ["repositoryURLs"] = new JsonArray("git@github.com:mona/LinkedList.git") };
            var json = SwiftManifest.Create("mona.LinkedList", "1.0.0", metadata, null, null, DateTime.UtcNow, new Dictionary<string, string>()).ToJson();

            var summary = SwiftManifestSummary.Parse(json);

            Assert.Empty(summary.Links);
            Assert.Null(summary.ToolsVersion);
            Assert.Null(summary.Author);
            Assert.False(summary.IsSigned);
        }
    }
}
