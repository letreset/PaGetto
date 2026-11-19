using PaGetto.Core.VsCode;
using Xunit;

namespace PaGetto.Core.Tests.VsCode;

public class VsCodeExtensionIdTests
{
    public class TryParse
    {
        [Theory]
        [InlineData("contoso.hello", "contoso", "hello")]
        [InlineData("Contoso.Hello-World_2", "Contoso", "Hello-World_2")]
        public void SplitsValidIdentifiers(string id, string publisher, string name)
        {
            Assert.True(VsCodeExtensionId.TryParse(id, out var parsedPublisher, out var parsedName));
            Assert.Equal(publisher, parsedPublisher);
            Assert.Equal(name, parsedName);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("contoso")]
        [InlineData(".hello")]
        [InlineData("contoso.")]
        [InlineData("contoso.hello.world")]
        [InlineData("contoso.-hello")]
        [InlineData("contoso.hel lo")]
        public void RefusesInvalidIdentifiers(string id)
        {
            Assert.False(VsCodeExtensionId.TryParse(id, out _, out _));
        }

        [Fact]
        public void RefusesTooLongIdentifiers()
        {
            Assert.False(VsCodeExtensionId.TryParse("contoso." + new string('a', VsCodeExtensionId.MaxLength), out _, out _));
        }
    }

    public class GetUuid
    {
        [Fact]
        public void IsStableAndIgnoresCase()
        {
            Assert.Equal(VsCodeExtensionId.GetUuid("extension", "contoso.hello"), VsCodeExtensionId.GetUuid("extension", "Contoso.Hello"));
            Assert.NotEqual(VsCodeExtensionId.GetUuid("extension", "contoso"), VsCodeExtensionId.GetUuid("publisher", "contoso"));
        }
    }
}
