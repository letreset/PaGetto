using PaGetto.Core.Go;
using Xunit;

namespace PaGetto.Core.Tests.Go;

public class GoModulePathTests
{
    public class IsValid
    {
        [Theory]
        [InlineData("example.com/tool")]
        [InlineData("github.com/Azure/azure-sdk-for-go")]
        [InlineData("example.com/tool/v2")]
        [InlineData("golang.org/x/mod")]
        [InlineData("example.com/a_b~c.d-e")]
        [InlineData("gopkg.in/yaml.v3")]
        [InlineData("example.com")]
        public void AcceptsModulePaths(string path)
        {
            Assert.True(GoModulePath.IsValid(path));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("tool")]
        [InlineData("Example.com/tool")]
        [InlineData("-example.com/tool")]
        [InlineData("/example.com/tool")]
        [InlineData("example.com/tool/")]
        [InlineData("example.com//tool")]
        [InlineData("example.com/../tool")]
        [InlineData("example.com/.tool")]
        [InlineData("example.com/tool.")]
        [InlineData("example.com/to ol")]
        [InlineData("example.com/to!ol")]
        [InlineData("example.com/tool@v1")]
        [InlineData("example.com/tööl")]
        [InlineData("example.com/tool/v1")]
        [InlineData("example.com/tool/v0")]
        [InlineData("example.com/tool/v02")]
        [InlineData("example.com/tool/v2.0")]
        [InlineData("example.com/con")]
        [InlineData("example.com/aux.go")]
        [InlineData("example.com/GOPROX~1")]
        public void RejectsInvalidPaths(string path)
        {
            Assert.False(GoModulePath.IsValid(path));
        }

        [Fact]
        public void RejectsPathsTooLongForTheDatabase()
        {
            Assert.True(GoModulePath.IsValid("example.com/" + new string('a', 116)));
            Assert.False(GoModulePath.IsValid("example.com/" + new string('a', 117)));
            Assert.False(GoModulePath.IsValid("example.com/" + new string('A', 60)));
        }
    }

    public class Escape
    {
        [Theory]
        [InlineData("github.com/Azure/sdk", "github.com/!azure/sdk")]
        [InlineData("example.com/tool", "example.com/tool")]
        [InlineData("v1.0.0-RC1", "v1.0.0-!r!c1")]
        public void EscapesUppercaseLetters(string text, string escaped)
        {
            Assert.Equal(escaped, GoModulePath.Escape(text));
            Assert.True(GoModulePath.TryUnescape(escaped, out var unescaped));
            Assert.Equal(text, unescaped);
        }

        [Theory]
        [InlineData("github.com/Azure/sdk")]
        [InlineData("github.com/!/sdk")]
        [InlineData("github.com/!1/sdk")]
        [InlineData("github.com/sdk!")]
        [InlineData(null)]
        public void UnescapeRejectsTextThatIsNotEscaped(string escaped)
        {
            Assert.False(GoModulePath.TryUnescape(escaped, out _));
        }
    }

    public class CheckMajorVersion
    {
        [Theory]
        [InlineData("example.com/tool", "v0.1.0")]
        [InlineData("example.com/tool", "v1.5.0")]
        [InlineData("example.com/tool/v2", "v2.0.0")]
        [InlineData("example.com/tool/v3", "v3.1.0-rc.1")]
        [InlineData("gopkg.in/yaml.v3", "v3.0.1")]
        public void AcceptsVersionsOfThePathsMajorVersion(string path, string version)
        {
            Assert.True(GoVersion.TryParse(version, out var parsed));
            Assert.Null(GoModulePath.CheckMajorVersion(path, parsed));
        }

        [Theory]
        [InlineData("example.com/tool", "v2.0.0", "no /v2 suffix")]
        [InlineData("example.com/tool/v2", "v1.0.0", "must be v2")]
        [InlineData("example.com/tool/v2", "v3.0.0", "must be v2")]
        [InlineData("example.com/tool", "v2.0.0+incompatible", "+incompatible")]
        public void RejectsVersionsOfAnotherMajorVersion(string path, string version, string message)
        {
            Assert.True(GoVersion.TryParse(version, out var parsed));
            Assert.Contains(message, GoModulePath.CheckMajorVersion(path, parsed));
        }
    }
}
