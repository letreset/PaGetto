using PaGetto.Core.Composer;
using Xunit;

namespace PaGetto.Core.Tests.Composer;

public class ComposerPackageNameTests
{
    public class IsValid
    {
        [Theory]
        [InlineData("monolog/monolog")]
        [InlineData("symfony/polyfill-php80")]
        [InlineData("acme.corp/my_package")]
        [InlineData("acme/package--name")]
        [InlineData("Acme/Hello")]
        [InlineData("a/b")]
        public void AcceptsComposerNames(string name)
        {
            Assert.True(ComposerPackageName.IsValid(name));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("monolog")]
        [InlineData("acme/hello/world")]
        [InlineData("acme/-hello")]
        [InlineData("acme/hello-")]
        [InlineData("acme--corp/hello")]
        [InlineData("acme/hel lo")]
        [InlineData("../hello")]
        [InlineData("acme/héllo")]
        public void RejectsEverythingElse(string name)
        {
            Assert.False(ComposerPackageName.IsValid(name));
        }

        [Fact]
        public void RejectsLongNames()
        {
            Assert.False(ComposerPackageName.IsValid("acme/" + new string('a', ComposerPackageName.MaxLength)));
        }
    }
}
