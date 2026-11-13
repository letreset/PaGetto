using System;
using PaGetto.Core.RubyGems;
using Xunit;

namespace PaGetto.Core.Tests.RubyGems;

public class RubyGemsCompactIndexTests
{
    private static RubyGemsSpecification Specification(string platform = "ruby")
    {
        return new RubyGemsSpecification
        {
            Name = "my_gem",
            Version = "1.2.0",
            Platform = platform,
            Sha256 = "abc123",
            RequiredRubyVersion = [">= 3.0"],
            Dependencies =
            [
                new RubyGemsDependency { Name = "rake", Requirement = ["~> 13.0"] },
                new RubyGemsDependency { Name = "json", Requirement = [">= 2.0", "< 3"] },
                new RubyGemsDependency { Name = "any" },
                new RubyGemsDependency { Name = "rspec", Requirement = ["~> 3.0"], Type = RubyGemsDependency.DevelopmentType },
            ],
        };
    }

    public class InfoLine
    {
        [Fact]
        public void ListsRuntimeDependenciesAndRequirements()
        {
            Assert.Equal(
                "1.2.0 rake:~> 13.0,json:>= 2.0&< 3,any:>= 0|checksum:abc123,ruby:>= 3.0",
                RubyGemsCompactIndex.InfoLine(Specification()));
        }

        [Fact]
        public void NamesThePlatformAfterTheVersion()
        {
            var specification = Specification("x86_64-linux");
            specification.Dependencies.Clear();
            specification.RequiredRubyVersion.Clear();
            specification.RequiredRubygemsVersion.Add(">= 3.3.22");

            Assert.Equal("1.2.0-x86_64-linux |checksum:abc123,rubygems:>= 3.3.22", RubyGemsCompactIndex.InfoLine(specification));
        }
    }

    public class TryParseVersionEntry
    {
        [Theory]
        [InlineData("1.2.0 rake:~> 13.0|checksum:abc", "1.2.0", "ruby")]
        [InlineData("1.2.0-x86_64-linux |checksum:abc", "1.2.0", "x86_64-linux")]
        [InlineData("1.2.0.rc1-java |checksum:abc", "1.2.0.rc1", "java")]
        public void SplitsTheVersionAndPlatform(string line, string version, string platform)
        {
            Assert.True(RubyGemsCompactIndex.TryParseVersionEntry(line, out var parsedVersion, out var parsedPlatform));
            Assert.Equal(version, parsedVersion);
            Assert.Equal(platform, parsedPlatform);
        }

        [Theory]
        [InlineData("---")]
        [InlineData("created_at: 2024-01-01")]
        [InlineData("1.0-")]
        public void RejectsLinesThatAreNotVersions(string line)
        {
            Assert.False(RubyGemsCompactIndex.TryParseVersionEntry(line, out _, out _));
        }
    }

    public class GetChecksum
    {
        [Theory]
        [InlineData("1.0 a:>= 0|checksum:ABC,ruby:>= 3.0", "abc")]
        [InlineData("1.0 |ruby:>= 3.0,checksum:def", "def")]
        [InlineData("1.0 |ruby:>= 3.0", null)]
        [InlineData("1.0", null)]
        public void FindsTheChecksum(string line, string checksum)
        {
            Assert.Equal(checksum, RubyGemsCompactIndex.GetChecksum(line));
        }
    }

    public class Files
    {
        [Fact]
        public void WritesTheHeaderAndLines()
        {
            Assert.Equal("---\n1.0 |checksum:a\n", RubyGemsCompactIndex.InfoFile(["1.0 |checksum:a"]));
            Assert.Equal("created_at: 2020-01-01T00:00:00Z\n---\n", RubyGemsCompactIndex.VersionsHeader(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.Equal("my_gem 1.0,1.1-java,-0.9 abc\n", RubyGemsCompactIndex.VersionsLine("my_gem", ["1.0", "1.1-java", "-0.9"], "abc"));
        }
    }
}
