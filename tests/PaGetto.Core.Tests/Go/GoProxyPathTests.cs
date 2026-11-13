using PaGetto.Core.Go;
using Xunit;

namespace PaGetto.Core.Tests.Go;

public class GoProxyPathTests
{
    public class TryParse
    {
        [Theory]
        [InlineData("example.com/tool/@v/list", GoProxyRequest.List, "example.com/tool", null)]
        [InlineData("github.com/!azure/sdk/@latest", GoProxyRequest.Latest, "github.com/Azure/sdk", null)]
        [InlineData("example.com/tool/@v/v1.0.0.info", GoProxyRequest.Info, "example.com/tool", "v1.0.0")]
        [InlineData("example.com/tool/@v/master.info", GoProxyRequest.Info, "example.com/tool", "master")]
        [InlineData("example.com/tool/@v/v1.0.0-!r!c1.mod", GoProxyRequest.Mod, "example.com/tool", "v1.0.0-RC1")]
        [InlineData("example.com/tool/v2/@v/v2.0.0+incompatible.zip", GoProxyRequest.Zip, "example.com/tool/v2", "v2.0.0+incompatible")]
        public void ReadsTheRequestModuleAndVersion(string path, GoProxyRequest request, string module, string version)
        {
            Assert.True(GoProxyPath.TryParse(path, out var parsed));
            Assert.Equal(request, parsed.Request);
            Assert.Equal(module, parsed.ModulePath);
            Assert.Equal(version, parsed.Version);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("example.com/tool")]
        [InlineData("example.com/tool/@v/")]
        [InlineData("example.com/tool/@v/v1.0.0")]
        [InlineData("example.com/tool/@v/v1.0.0.tar.gz")]
        [InlineData("example.com/tool/@v/.zip")]
        [InlineData("example.com/tool/@v/a/b.zip")]
        [InlineData("/@v/list")]
        [InlineData("@latest")]
        [InlineData("github.com/Azure/sdk/@v/list")]
        [InlineData("example.com/tool/@v/v1.0.0-RC1.info")]
        public void RejectsOtherPaths(string path)
        {
            Assert.False(GoProxyPath.TryParse(path, out _));
        }
    }
}
