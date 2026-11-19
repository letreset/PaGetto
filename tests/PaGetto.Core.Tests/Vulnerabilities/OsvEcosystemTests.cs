using PaGetto.Core.Entities;
using PaGetto.Core.Vulnerabilities;
using Xunit;

namespace PaGetto.Core.Tests.Vulnerabilities;

public class OsvEcosystemTests
{
    public class GetName
    {
        [Theory]
        [InlineData(FeedType.NuGet, "NuGet")]
        [InlineData(FeedType.Npm, "npm")]
        [InlineData(FeedType.Cargo, "crates.io")]
        [InlineData(FeedType.Composer, "Packagist")]
        [InlineData(FeedType.Ivy, "Maven")]
        [InlineData(FeedType.CocoaPods, null)]
        [InlineData(FeedType.Swift, null)]
        [InlineData(FeedType.Vsix, null)]
        public void MapsFeedTypesToOsvEcosystems(FeedType feedType, string expected)
        {
            Assert.Equal(expected, OsvEcosystem.GetName(feedType));
        }

        [Fact]
        public void EverySupportedFeedTypeHasAnEcosystem()
        {
            Assert.All(OsvEcosystem.SupportedFeedTypes, t => Assert.NotNull(OsvEcosystem.GetName(t)));
        }
    }

    public class GetPackageName
    {
        [Fact]
        public void UnescapesGoModulePaths()
        {
            Assert.Equal("github.com/BurntSushi/toml", OsvEcosystem.GetPackageName(FeedType.Go, "github.com/!burnt!sushi/toml"));
        }

        [Fact]
        public void TakesTheMavenCoordinatesOfIvyModules()
        {
            Assert.Equal("org.scala-sbt:sbt-plugin", OsvEcosystem.GetPackageName(FeedType.Ivy, "org.scala-sbt:sbt-plugin:scala_2.12:sbt_1.0"));
        }

        [Fact]
        public void TakesThePublishedCaseOfCranPackages()
        {
            var manifest = """{"Package":"Rcpp","Version":"1.0.0"}""";

            Assert.Equal("Rcpp", OsvEcosystem.GetPackageName(FeedType.Cran, "rcpp", manifest));
            Assert.Equal("rcpp", OsvEcosystem.GetPackageName(FeedType.Cran, "rcpp", null));
        }

        [Fact]
        public void KeepsOtherNames()
        {
            Assert.Equal("@scope/name", OsvEcosystem.GetPackageName(FeedType.Npm, "@scope/name"));
        }
    }
}
