using PaGetto.Core.Entities;
using PaGetto.Core.Sbom;
using Xunit;

namespace PaGetto.Core.Tests.Sbom;

public class PackageUrlTests
{
    public class Create
    {
        [Theory]
        [InlineData(FeedType.NuGet, "Newtonsoft.Json", "13.0.1", "pkg:nuget/Newtonsoft.Json@13.0.1")]
        [InlineData(FeedType.Pub, "my_package", "1.0.0", "pkg:pub/my_package@1.0.0")]
        [InlineData(FeedType.Npm, "@Scope/Thing", "1.0.0", "pkg:npm/%40scope/thing@1.0.0")]
        [InlineData(FeedType.Maven, "org.example:my-lib", "1.0", "pkg:maven/org.example/my-lib@1.0")]
        [InlineData(FeedType.Ivy, "com.example:sbt-tool:scala_2.12:sbt_1.0", "1.0.0", "pkg:maven/com.example/sbt-tool@1.0.0")]
        [InlineData(FeedType.PyPI, "My_Package", "1.0", "pkg:pypi/my-package@1.0")]
        [InlineData(FeedType.Cargo, "serde", "1.0.0+build", "pkg:cargo/serde@1.0.0%2Bbuild")]
        [InlineData(FeedType.Go, "github.com/Acme/lib/v2", "v2.1.0", "pkg:golang/github.com/Acme/lib/v2@v2.1.0")]
        [InlineData(FeedType.RubyGems, "Rails", "7.1.0", "pkg:gem/Rails@7.1.0")]
        [InlineData(FeedType.Hex, "Plug", "1.15.0", "pkg:hex/plug@1.15.0")]
        [InlineData(FeedType.Composer, "Acme/Hello", "1.0.0", "pkg:composer/acme/hello@1.0.0")]
        [InlineData(FeedType.CocoaPods, "Firebase/Core", "10.0.0", "pkg:cocoapods/Firebase@10.0.0#Core")]
        [InlineData(FeedType.Swift, "mona.LinkedList", "1.1.1", "pkg:swift/mona/LinkedList@1.1.1")]
        [InlineData(FeedType.Cran, "Hello", "1.0", "pkg:cran/Hello@1.0")]
        public void FollowsThePurlTypeOfTheEcosystem(FeedType feedType, string name, string version, string expected)
        {
            Assert.Equal(expected, PackageUrl.Create(feedType, name, version));
        }

        [Fact]
        public void LeavesTheVersionOutForAPackage()
        {
            Assert.Equal("pkg:npm/left-pad", PackageUrl.Create(FeedType.Npm, "left-pad", null));
        }

        [Theory]
        [InlineData(FeedType.Vsix)]
        [InlineData(FeedType.VsCode)]
        public void IsNullWithoutAPurlType(FeedType feedType)
        {
            Assert.Null(PackageUrl.Create(feedType, "acme.hello", "1.0.0"));
        }
    }
}
