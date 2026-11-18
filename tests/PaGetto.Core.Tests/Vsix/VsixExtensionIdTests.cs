using PaGetto.Core.Vsix;
using Xunit;

namespace PaGetto.Core.Tests.Vsix;

public class VsixExtensionIdTests
{
    public class IsValid
    {
        [Theory]
        [InlineData("HelloTools")]
        [InlineData("Contoso.HelloTools")]
        [InlineData("SelectionHighlight..a14874d2-8199-4a60-af8a-11d6447813aa")]
        [InlineData("a14874d2-8199-4a60-af8a-11d6447813aa")]
        [InlineData("Hello_Tools.2022")]
        [InlineData("x")]
        public void AcceptsIdentifiersOfExtensions(string id)
        {
            Assert.True(VsixExtensionId.IsValid(id));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(".hidden")]
        [InlineData("trailing.")]
        [InlineData("..")]
        [InlineData("with space")]
        [InlineData("with/slash")]
        [InlineData("with\\backslash")]
        [InlineData("with:colon")]
        [InlineData("café")]
        public void RejectsIdentifiersThatAreNotSafeInUrlsAndPaths(string id)
        {
            Assert.False(VsixExtensionId.IsValid(id));
        }

        [Fact]
        public void RejectsIdentifiersLongerThanTheDatabaseColumn()
        {
            Assert.True(VsixExtensionId.IsValid(new string('a', VsixExtensionId.MaxLength)));
            Assert.False(VsixExtensionId.IsValid(new string('a', VsixExtensionId.MaxLength + 1)));
        }
    }

    public class Normalize
    {
        [Fact]
        public void LowercasesTheIdentifier()
        {
            Assert.Equal("contoso.hellotools", VsixExtensionId.Normalize("Contoso.HelloTools"));
        }
    }
}
