using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using PaGetto.Core.Maven;
using Xunit;

namespace PaGetto.Core.Tests.Maven;

public class MavenMetadataXmlTests
{
    private static readonly DateTime Time = new(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    public class WriteArtifact
    {
        [Fact]
        public void ListsTheVersionsWithTheNewestAndTheNewestRelease()
        {
            var xml = Parse(MavenMetadataXml.WriteArtifact("com.example", "my-lib", ["1.0", "1.1", "2.0-SNAPSHOT"], Time, []));

            Assert.Equal("com.example", xml.Element("groupId")?.Value);
            Assert.Equal("my-lib", xml.Element("artifactId")?.Value);

            var versioning = xml.Element("versioning");
            Assert.Equal("2.0-SNAPSHOT", versioning?.Element("latest")?.Value);
            Assert.Equal("1.1", versioning?.Element("release")?.Value);
            Assert.Equal(new[] { "1.0", "1.1", "2.0-SNAPSHOT" }, versioning?.Element("versions")?.Elements("version").Select(v => v.Value));
            Assert.Equal("20240102030405", versioning?.Element("lastUpdated")?.Value);
        }

        [Fact]
        public void WritesThePluginsOfAGroup()
        {
            MavenMetadata.Plugin[] plugins = [new() { Name = "Clean", Prefix = "clean", ArtifactId = "maven-clean-plugin" }];

            var xml = Parse(MavenMetadataXml.WriteArtifact("org.apache.maven", "plugins", [], Time, plugins));

            Assert.Null(xml.Element("versioning"));
            var plugin = Assert.Single(xml.Element("plugins")!.Elements("plugin"));
            Assert.Equal("clean", plugin.Element("prefix")?.Value);
            Assert.Equal("maven-clean-plugin", plugin.Element("artifactId")?.Value);
        }

        [Fact]
        public void WritesTheSameBytesForTheSameState()
        {
            Assert.Equal(
                MavenMetadataXml.WriteArtifact("g", "a", ["1.0"], Time, []),
                MavenMetadataXml.WriteArtifact("g", "a", ["1.0"], Time, []));
        }
    }

    public class WriteSnapshot
    {
        [Fact]
        public void PointsAtTheNewestBuildOfEachFile()
        {
            var xml = Parse(MavenMetadataXml.WriteSnapshot("com.example", "my-lib", "1.0-SNAPSHOT",
            [
                (File("my-lib-1.0-20240101.000000-1.jar"), Time.AddDays(-1)),
                (File("my-lib-1.0-20240101.000000-1.pom"), Time.AddDays(-1)),
                (File("my-lib-1.0-20240102.030405-2.jar"), Time),
                (File("my-lib-1.0-20240102.030405-2-sources.jar"), Time),
            ]));

            Assert.Equal("1.1.0", xml.Attribute("modelVersion")?.Value);
            Assert.Equal("1.0-SNAPSHOT", xml.Element("version")?.Value);

            var versioning = xml.Element("versioning")!;
            Assert.Equal("20240102.030405", versioning.Element("snapshot")?.Element("timestamp")?.Value);
            Assert.Equal("2", versioning.Element("snapshot")?.Element("buildNumber")?.Value);
            Assert.Equal("20240102030405", versioning.Element("lastUpdated")?.Value);

            var files = versioning.Element("snapshotVersions")!.Elements("snapshotVersion")
                .Select(v => $"{v.Element("classifier")?.Value}|{v.Element("extension")?.Value}|{v.Element("value")?.Value}")
                .ToList();

            Assert.Equal(new[] { "|jar|1.0-20240102.030405-2", "sources|jar|1.0-20240102.030405-2", "|pom|1.0-20240101.000000-1" }, files);
        }

        private static MavenFileName File(string fileName)
        {
            Assert.True(MavenFileName.TryParse("my-lib", "1.0-SNAPSHOT", fileName, out var file));
            return file;
        }
    }

    public class Read
    {
        [Fact]
        public void ReadsVersionsAndPlugins()
        {
            var metadata = MavenMetadataXml.Read(new MemoryStream(Encoding.UTF8.GetBytes("""
                <?xml version="1.0" encoding="UTF-8"?>
                <metadata>
                  <groupId>org.example</groupId>
                  <artifactId>lib</artifactId>
                  <versioning>
                    <latest>2.0</latest>
                    <versions><version>1.0</version><version> 2.0 </version></versions>
                    <lastUpdated>20240102030405</lastUpdated>
                  </versioning>
                  <plugins><plugin><prefix>x</prefix><artifactId>x-maven-plugin</artifactId></plugin></plugins>
                </metadata>
                """)));

            Assert.Equal("org.example", metadata.GroupId);
            Assert.Equal(new[] { "1.0", "2.0" }, metadata.Versions);
            Assert.Equal(Time, metadata.LastUpdated);
            Assert.Equal("x-maven-plugin", Assert.Single(metadata.Plugins).ArtifactId);
        }
    }

    private static XElement Parse(byte[] xml)
    {
        var root = XDocument.Parse(Encoding.UTF8.GetString(xml)).Root!;
        Assert.Equal("metadata", root.Name.LocalName);
        return root;
    }
}
