using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using PaGetto.Core.Maven;
using Xunit;

namespace PaGetto.Core.Tests.Maven;

public class MavenPomReaderTests
{
    public class Read
    {
        [Fact]
        public void ReadsTheManifestOfAPom()
        {
            var manifest = ReadPom("""
                <?xml version="1.0" encoding="UTF-8"?>
                <project xmlns="http://maven.apache.org/POM/4.0.0">
                  <modelVersion>4.0.0</modelVersion>
                  <groupId>com.example</groupId>
                  <artifactId>my-lib</artifactId>
                  <version>1.0</version>
                  <name>My library</name>
                  <description>
                    Does things,
                    well.
                  </description>
                  <url>https://example.com/my-lib</url>
                  <licenses><license><name>MIT</name></license></licenses>
                  <scm><url>https://github.com/example/my-lib</url></scm>
                  <issueManagement><url>https://github.com/example/my-lib/issues</url></issueManagement>
                  <dependencies>
                    <dependency><groupId>org.slf4j</groupId><artifactId>slf4j-api</artifactId><version>2.0.9</version></dependency>
                    <dependency><groupId>org.junit</groupId><artifactId>junit</artifactId><scope>test</scope><optional>true</optional></dependency>
                  </dependencies>
                </project>
                """);

            Assert.Equal("com.example", MavenPomReader.GetString(manifest, "groupId"));
            Assert.Equal("my-lib", MavenPomReader.GetString(manifest, "artifactId"));
            Assert.Equal("1.0", MavenPomReader.GetString(manifest, "version"));
            Assert.Equal("jar", MavenPomReader.GetString(manifest, "packaging"));
            Assert.Equal("Does things, well.", MavenPomReader.GetDescription(manifest, 4000));

            var summary = MavenPomSummary.Parse(manifest.ToJsonString());
            Assert.Equal("com.example:my-lib", summary.Coordinates);
            Assert.Equal("MIT", summary.License);
            Assert.Equal("https://example.com/my-lib", summary.Links["homepage"]);
            Assert.Equal("https://github.com/example/my-lib", summary.Links["repository"]);
            Assert.Equal("https://github.com/example/my-lib/issues", summary.Links["issue_tracker"]);
            Assert.Collection(
                summary.Dependencies,
                d => Assert.Equal(("org.slf4j:slf4j-api", "2.0.9"), (d.Key, d.Value)),
                d => Assert.Equal(("org.junit:junit", "managed · test · optional"), (d.Key, d.Value)));
        }

        [Fact]
        public void InheritsTheGroupAndVersionFromTheParent()
        {
            var manifest = ReadPom("""
                <project>
                  <parent><groupId>com.example</groupId><artifactId>parent</artifactId><version>2.0</version></parent>
                  <artifactId>child</artifactId>
                  <packaging>pom</packaging>
                </project>
                """);

            Assert.Equal("com.example", MavenPomReader.GetString(manifest, "groupId"));
            Assert.Equal("2.0", MavenPomReader.GetString(manifest, "version"));
            Assert.Equal("pom", MavenPomReader.GetString(manifest, "packaging"));
            Assert.Null(MavenPomReader.GetDescription(manifest, 4000));
        }

        [Fact]
        public void UsesTheNameWhenThereIsNoDescription()
        {
            var manifest = ReadPom("<project><artifactId>a</artifactId><name>The name</name></project>");

            Assert.Equal("The name", MavenPomReader.GetDescription(manifest, 4000));
        }

        [Theory]
        [InlineData("not xml")]
        [InlineData("<project>")]
        [InlineData("<settings></settings>")]
        public void RejectsWhatIsNotAPom(string content)
        {
            Assert.Throws<InvalidMavenPomException>(() => ReadPom(content));
        }

        [Fact]
        public void DoesNotResolveEntities()
        {
            var content = """
                <?xml version="1.0"?>
                <!DOCTYPE project [<!ENTITY secret SYSTEM "file:///etc/passwd">]>
                <project><artifactId>&secret;</artifactId></project>
                """;

            Assert.Throws<InvalidMavenPomException>(() => ReadPom(content));
        }

        private static JsonObject ReadPom(string content)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            return MavenPomReader.Read(stream);
        }
    }
}
