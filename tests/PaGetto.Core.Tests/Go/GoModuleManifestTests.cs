using System;
using PaGetto.Core.Go;
using Xunit;

namespace PaGetto.Core.Tests.Go;

public class GoModuleManifestTests
{
    public class Parse
    {
        [Fact]
        public void ReadsWhatCreateWrites()
        {
            var json = GoModuleManifest.Create("example.com/Tool", new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc), "module example.com/Tool\n");

            var manifest = GoModuleManifest.Parse(json);

            Assert.Equal("example.com/Tool", manifest.Module);
            Assert.Equal(new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc), manifest.Time);
            Assert.Equal(DateTimeKind.Utc, manifest.Time.Value.Kind);
            Assert.Equal("module example.com/Tool\n", manifest.Mod);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("[]")]
        [InlineData("{not json")]
        [InlineData("{\"module\":1,\"time\":\"never\"}")]
        public void GivesAnEmptyManifestForAnythingElse(string json)
        {
            var manifest = GoModuleManifest.Parse(json);

            Assert.Null(manifest.Module);
            Assert.Null(manifest.Time);
            Assert.Null(manifest.Mod);
        }
    }

    public class CreateInfo
    {
        [Fact]
        public void WritesTheVersionAndTimeAsTheGoCommandReadsThem()
        {
            Assert.Equal(
                "{\"Version\":\"v1.0.0\",\"Time\":\"2020-01-02T03:04:05Z\"}",
                GoModuleManifest.CreateInfo("v1.0.0", new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc)));
        }
    }
}
