using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using PaGetto.Core.Ivy;
using Xunit;

namespace PaGetto.Core.Tests.Ivy;

public class IvyDescriptorReaderTests
{
    // An ivy.xml as sbt 1.x writes it for an sbt plugin.
    private const string PluginDescriptor = """
        <?xml version="1.0" encoding="UTF-8"?>
        <ivy-module version="2.0" xmlns:e="http://ant.apache.org/ivy/extra">
            <info organisation="com.example" module="sbt-tool" revision="1.0.0" status="release" publication="20240101120000" e:sbtVersion="1.0" e:scalaVersion="2.12">
                <license name="Apache-2.0" url="https://www.apache.org/licenses/LICENSE-2.0.txt" />
                <description homepage="https://example.com/sbt-tool">
                    An sbt plugin
                    for tests.
                </description>
            </info>
            <configurations>
                <conf name="compile" visibility="public" description="" />
            </configurations>
            <publications>
                <artifact name="sbt-tool" type="jar" ext="jar" conf="compile" />
            </publications>
            <dependencies>
                <dependency org="org.scala-lang" name="scala-library" rev="2.12.20" conf="provided->default(compile)" />
                <dependency org="com.example" name="sbt-other" rev="0.5.0" conf="compile->default(compile)" e:sbtVersion="1.0" e:scalaVersion="2.12" />
            </dependencies>
        </ivy-module>
        """;

    public class Read
    {
        [Fact]
        public void ReadsTheCoordinatesDescriptionLicensesAndDependencies()
        {
            var manifest = IvyDescriptorReader.Read(Stream(PluginDescriptor));

            Assert.Equal("com.example", IvyDescriptorReader.GetString(manifest, "organisation"));
            Assert.Equal("sbt-tool", IvyDescriptorReader.GetString(manifest, "module"));
            Assert.Equal("1.0.0", IvyDescriptorReader.GetString(manifest, "revision"));
            Assert.Equal("2.12", IvyDescriptorReader.GetString(manifest, "scalaVersion"));
            Assert.Equal("1.0", IvyDescriptorReader.GetString(manifest, "sbtVersion"));
            Assert.Equal("release", IvyDescriptorReader.GetString(manifest, "status"));
            Assert.Equal("https://example.com/sbt-tool", IvyDescriptorReader.GetString(manifest, "homepage"));
            Assert.Equal("An sbt plugin for tests.", IvyDescriptorReader.GetDescription(manifest, 4000));
            Assert.Equal(new[] { "Apache-2.0" }, manifest["licenses"]!.AsArray().Select(l => l!.GetValue<string>()));

            var dependencies = manifest["dependencies"]!.AsArray().Cast<JsonObject>().ToList();
            Assert.Equal(2, dependencies.Count);
            Assert.Equal("scala-library", IvyDescriptorReader.GetString(dependencies[0], "name"));
            Assert.Null(IvyDescriptorReader.GetString(dependencies[0], "sbtVersion"));
            Assert.Equal("1.0", IvyDescriptorReader.GetString(dependencies[1], "sbtVersion"));
        }

        [Theory]
        [InlineData("not xml")]
        [InlineData("<project />")]
        [InlineData("<ivy-module version=\"2.0\" />")]
        public void RejectsWhatIsNotAnIvyFile(string content)
        {
            Assert.Throws<InvalidIvyDescriptorException>(() => IvyDescriptorReader.Read(Stream(content)));
        }
    }

    public class CheckCoordinates
    {
        [Fact]
        public void AcceptsTheDescriptorOfThePathsRevision()
        {
            Assert.True(IvyPath.TryParse("com.example/sbt-tool/scala_2.12/sbt_1.0/1.0.0/ivys/ivy.xml", out var path));

            IvyDescriptorReader.CheckCoordinates(IvyDescriptorReader.Read(Stream(PluginDescriptor)), path);
        }

        [Theory]
        [InlineData("com.example/sbt-tool/scala_2.12/sbt_1.0/2.0.0/ivys/ivy.xml")]
        [InlineData("com.example/sbt-other/scala_2.12/sbt_1.0/1.0.0/ivys/ivy.xml")]
        [InlineData("com.example/sbt-tool/scala_2.10/sbt_0.13/1.0.0/ivys/ivy.xml")]
        [InlineData("com.example/sbt-tool/1.0.0/ivys/ivy.xml")]
        public void RejectsTheDescriptorOfAnotherModuleOrRevision(string value)
        {
            Assert.True(IvyPath.TryParse(value, out var path));
            var manifest = IvyDescriptorReader.Read(Stream(PluginDescriptor));

            var exception = Assert.Throws<InvalidIvyDescriptorException>(() => IvyDescriptorReader.CheckCoordinates(manifest, path));
            Assert.Contains("com.example:sbt-tool:scala_2.12:sbt_1.0 1.0.0", exception.Message);
        }
    }

    private static MemoryStream Stream(string content)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(content));
    }
}
