using PaGetto.Core.Swift;
using Xunit;

namespace PaGetto.Core.Tests.Swift;

public class SwiftReleaseMetadataTests
{
    public class Read
    {
        [Fact]
        public void KeepsTheMetadataAsPublished()
        {
            var metadata = SwiftReleaseMetadata.Read("""
                {
                  "description": "A linked list",
                  "licenseURL": "https://example.com/LICENSE",
                  "repositoryURLs": ["https://github.com/mona/LinkedList"],
                  "author": {"name": "Mona", "organization": {"name": "Acme", "url": "https://acme.example"}},
                  "custom": [1, 2]
                }
                """);

            Assert.Equal("A linked list", metadata["description"]!.GetValue<string>());
            Assert.Equal("Acme", metadata["author"]!["organization"]!["name"]!.GetValue<string>());
            Assert.Equal("[1,2]", metadata["custom"]!.ToJsonString());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public void NoMetadataIsAnEmptyObject(string json)
        {
            Assert.Empty(SwiftReleaseMetadata.Read(json));
        }

        [Fact]
        public void WritesThePublicationTimeAsSwiftPackageManagerReadsIt()
        {
            var metadata = SwiftReleaseMetadata.Read("""{"originalPublicationTime": "2023-02-16T04:00:00.123+02:00"}""");

            Assert.Equal("2023-02-16T02:00:00Z", metadata["originalPublicationTime"]!.GetValue<string>());
        }

        [Theory]
        [InlineData("[]", "not a JSON object")]
        [InlineData("{", "not valid JSON")]
        [InlineData("""{"description": 1}""", "description must be a string")]
        [InlineData("""{"repositoryURLs": "https://github.com/mona/LinkedList"}""", "repositoryURLs must be an array")]
        [InlineData("""{"repositoryURLs": [1]}""", "repositoryURLs must be an array")]
        [InlineData("""{"author": "Mona"}""", "author must be an object with a name")]
        [InlineData("""{"author": {"email": "mona@example.com"}}""", "author must be an object with a name")]
        [InlineData("""{"author": {"name": "Mona", "url": 1}}""", "author.url must be a string")]
        [InlineData("""{"author": {"name": "Mona", "organization": {}}}""", "author.organization must be an object with a name")]
        [InlineData("""{"originalPublicationTime": "yesterday"}""", "originalPublicationTime must be an ISO 8601")]
        public void RefusesFieldsSwiftPackageManagerCanNotRead(string json, string message)
        {
            var e = Assert.Throws<InvalidSwiftPackageException>(() => SwiftReleaseMetadata.Read(json));

            Assert.Contains(message, e.Message);
        }
    }

    public class NormalizeRepositoryUrl
    {
        [Theory]
        [InlineData("https://github.com/mona/LinkedList")]
        [InlineData("https://github.com/mona/LinkedList.git")]
        [InlineData("https://github.com/mona/LinkedList/")]
        [InlineData("http://github.com/mona/linkedlist")]
        [InlineData("git@github.com:mona/LinkedList.git")]
        [InlineData("ssh://git@github.com/mona/LinkedList.git")]
        [InlineData("github.com/mona/LinkedList")]
        public void WaysOfWritingARepositoryAreTheSame(string url)
        {
            Assert.Equal("github.com/mona/linkedlist", SwiftReleaseMetadata.NormalizeRepositoryUrl(url));
        }

        [Fact]
        public void KeepsThePort()
        {
            Assert.Equal("git.example.com:8443/mona/linkedlist", SwiftReleaseMetadata.NormalizeRepositoryUrl("https://git.example.com:8443/mona/LinkedList"));
        }
    }
}
