using PaGetto.Core.Go;
using Xunit;

namespace PaGetto.Core.Tests.Go;

public class GoModFileTests
{
    public class Parse
    {
        [Fact]
        public void ReadsTheModuleGoVersionToolchainAndRequirements()
        {
            var mod = GoModFile.Parse(
                "// The tool.\n" +
                "module \"example.com/Tool/v2\" // comment\r\n" +
                "\n" +
                "go 1.22\n" +
                "toolchain go1.22.1\n" +
                "\n" +
                "require golang.org/x/mod v0.17.0\n" +
                "\n" +
                "require (\n" +
                "\tgithub.com/Azure/sdk v1.2.3\n" +
                "\tgolang.org/x/text v0.14.0 // indirect\n" +
                "\texample.com/other v1.0.0 // indirect; needed by tests\n" +
                ")\n" +
                "\n" +
                "replace example.com/other => ../other\n" +
                "exclude example.com/bad v1.0.0\n");

            Assert.Equal("example.com/Tool/v2", mod.Module);
            Assert.Equal("1.22", mod.GoVersion);
            Assert.Equal("go1.22.1", mod.Toolchain);
            Assert.Equal(
                new[]
                {
                    new GoRequirement("golang.org/x/mod", "v0.17.0", false),
                    new GoRequirement("github.com/Azure/sdk", "v1.2.3", false),
                    new GoRequirement("golang.org/x/text", "v0.14.0", true),
                    new GoRequirement("example.com/other", "v1.0.0", true),
                },
                mod.Requires);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not a go.mod file (")]
        public void GivesAnEmptyFileForAnythingElse(string text)
        {
            var mod = GoModFile.Parse(text);

            Assert.Null(mod.Module);
            Assert.Null(mod.GoVersion);
            Assert.Empty(mod.Requires);
        }
    }
}
