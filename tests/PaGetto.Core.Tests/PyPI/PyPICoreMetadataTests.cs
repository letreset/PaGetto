using System.Linq;
using System.Text.Json.Nodes;
using PaGetto.Core.PyPI;
using Xunit;

namespace PaGetto.Core.Tests.PyPI;

public class PyPICoreMetadataTests
{
    public class Read
    {
        [Fact]
        public void ReadsFieldsAsPep566ConvertsThemToJson()
        {
            var manifest = PyPICoreMetadata.Read(
                """
                Metadata-Version: 2.3
                Name: My_Package
                Version: 1.0
                Summary: A test package.
                Requires-Python: >=3.9
                Requires-Dist: requests>=2.0
                Requires-Dist: rich; extra == "cli"
                Project-URL: Source, https://example.com/src
                Classifier: License :: OSI Approved :: MIT License
                Keywords: one,two, three
                Description-Content-Type: text/markdown

                # My package

                The readme.
                """,
                out var description);

            Assert.Equal("2.3", PyPICoreMetadata.GetString(manifest, "metadata_version"));
            Assert.Equal("My_Package", PyPICoreMetadata.GetString(manifest, "name"));
            Assert.Equal("1.0", PyPICoreMetadata.GetString(manifest, "version"));
            Assert.Equal("A test package.", PyPICoreMetadata.GetString(manifest, "summary"));
            Assert.Equal(">=3.9", PyPICoreMetadata.GetString(manifest, "requires_python"));
            Assert.Equal(new[] { "requests>=2.0", "rich; extra == \"cli\"" }, PyPICoreMetadata.GetStrings(manifest, "requires_dist"));
            Assert.Equal(new[] { "Source, https://example.com/src" }, PyPICoreMetadata.GetStrings(manifest, "project_url"));
            Assert.Equal(new[] { "License :: OSI Approved :: MIT License" }, PyPICoreMetadata.GetStrings(manifest, "classifier"));
            Assert.Equal(new[] { "one", "two", "three" }, PyPICoreMetadata.GetStrings(manifest, "keywords"));
            Assert.Equal("text/markdown", PyPICoreMetadata.GetString(manifest, "description_content_type"));

            // The description is the readme, not part of the manifest.
            Assert.Equal("# My package\n\nThe readme.", description);
            Assert.False(manifest.ContainsKey("description"));
        }

        [Fact]
        public void ReadsADescriptionHeaderWithFoldedLines()
        {
            var manifest = PyPICoreMetadata.Read(
                "Metadata-Version: 1.1\r\nName: old\r\nVersion: 0.1\r\nDescription: First line\r\n       |\r\n       |Second line\r\n        Third line\r\nKeywords: a b\r\n",
                out var description);

            Assert.Equal("old", PyPICoreMetadata.GetString(manifest, "name"));
            Assert.Equal("First line\n\nSecond line\nThird line", description);
            Assert.Equal(new[] { "a", "b" }, PyPICoreMetadata.GetStrings(manifest, "keywords"));
        }

        [Fact]
        public void KeepsTheFirstValueOfAFieldThatMayAppearOnce()
        {
            var manifest = PyPICoreMetadata.Read("Name: first\nName: second\n", out var description);

            Assert.Equal("first", PyPICoreMetadata.GetString(manifest, "name"));
            Assert.Null(description);
        }

        [Fact]
        public void SkipsLinesThatAreNotFields()
        {
            var manifest = PyPICoreMetadata.Read("not a field\nName: pkg\n", out _);

            Assert.Equal(new[] { "name" }, manifest.Select(p => p.Key));
        }
    }

    public class CreateManifest
    {
        [Fact]
        public void HasTheNameAndVersion()
        {
            var manifest = PyPICoreMetadata.CreateManifest("my-package", "1.0");

            Assert.Equal("my-package", PyPICoreMetadata.GetString(manifest, "name"));
            Assert.Equal("1.0", PyPICoreMetadata.GetString(manifest, "version"));
        }
    }

    public class GetStrings
    {
        [Fact]
        public void SkipsValuesThatAreNotStrings()
        {
            var manifest = JsonNode.Parse("""{ "classifier": ["a", 1, null, " ", "b"] }""") as JsonObject;

            Assert.Equal(new[] { "a", "b" }, PyPICoreMetadata.GetStrings(manifest, "classifier"));
            Assert.Empty(PyPICoreMetadata.GetStrings(manifest, "missing"));
        }
    }
}
