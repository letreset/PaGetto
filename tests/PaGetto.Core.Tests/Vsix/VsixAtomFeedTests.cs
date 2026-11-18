using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using PaGetto.Core.Vsix;
using Xunit;

namespace PaGetto.Core.Tests.Vsix;

public class VsixAtomFeedTests
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Vsix = "http://schemas.microsoft.com/developer/vsx-syndication-schema/2010";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    private static VsixGalleryEntry Entry()
    {
        return new VsixGalleryEntry
        {
            Manifest = new VsixManifest
            {
                Id = "Contoso.HelloTools",
                Version = "1.2.0",
                Publisher = "Contoso",
                DisplayName = "Hello Tools",
                Description = "Says hello.",
                MoreInfo = "https://github.com/contoso/hello-tools",
                ReleaseNotes = "ReleaseNotes.txt",
                InstallationTargets =
                [
                    new VsixInstallationTarget { Id = "Microsoft.VisualStudio.Community", Version = "[17.0,18.0)", ProductArchitecture = "amd64" },
                    new VsixInstallationTarget { Id = "Microsoft.VisualStudio.Pro", Version = "[16.0,17.0)" },
                ],
            },
            Published = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            Updated = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            Downloads = 42,
            DownloadUrl = "https://example.com/feeds/vs/vsix/extensions/Contoso.HelloTools/1.2.0/Contoso.HelloTools-1.2.0.vsix",
            IconUrl = "https://example.com/feeds/vs/vsix/extensions/Contoso.HelloTools/1.2.0/icon",
        };
    }

    public class Write
    {
        [Fact]
        public void WritesTheEntriesAsVisualStudioReadsThem()
        {
            var xml = VsixAtomFeed.Write("uuid:1", "Contoso", "https://example.com/feeds/vs/vsix/atom.xml", new DateTime(2020, 1, 3, 0, 0, 0, DateTimeKind.Utc), [Entry()]);

            Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", xml);

            var feed = XDocument.Parse(xml).Root!;
            Assert.Equal(Atom + "feed", feed.Name);
            Assert.Equal("Contoso", feed.Element(Atom + "title")!.Value);
            Assert.Equal("2020-01-03T00:00:00Z", feed.Element(Atom + "updated")!.Value);

            var entry = Assert.Single(feed.Elements(Atom + "entry"));
            Assert.Equal("Contoso.HelloTools", entry.Element(Atom + "id")!.Value);
            Assert.Equal("Hello Tools", entry.Element(Atom + "title")!.Value);
            Assert.Equal("Says hello.", entry.Element(Atom + "summary")!.Value);
            Assert.Equal("2020-01-02T03:04:05Z", entry.Element(Atom + "updated")!.Value);
            Assert.Equal("Contoso", entry.Element(Atom + "author")!.Element(Atom + "name")!.Value);
            Assert.Equal(Entry().DownloadUrl, (string)entry.Element(Atom + "content")!.Attribute("src"));
            Assert.Equal(Entry().IconUrl, (string)entry.Elements(Atom + "link").Single(l => (string)l.Attribute("rel") == "icon").Attribute("href"));
            Assert.DoesNotContain(entry.Elements(Atom + "link"), l => (string)l.Attribute("rel") == "previewimage");

            var vsix = entry.Element(Vsix + "Vsix")!;
            Assert.Equal("Contoso.HelloTools", vsix.Element(Vsix + "Id")!.Value);
            Assert.Equal("1.2.0", vsix.Element(Vsix + "Version")!.Value);
            Assert.Equal("true", (string)vsix.Element(Vsix + "Rating")!.Attribute(Xsi + "nil"));
            Assert.Equal("42", vsix.Element(Vsix + "DownloadCount")!.Value);
            Assert.Equal(new[] { "Microsoft.VisualStudio.Community", "Microsoft.VisualStudio.Pro" }, vsix.Elements(Vsix + "Installations").Select(i => i.Element(Vsix + "Identifier")!.Value));
            Assert.Equal("amd64", vsix.Elements(Vsix + "Installations").First().Element(Vsix + "ProductArchitecture")!.Value);
            Assert.Equal("https://github.com/contoso/hello-tools", vsix.Element(Vsix + "MoreInfo")!.Value);

            // Release notes that are a file in the extension are not a link.
            Assert.Null(vsix.Element(Vsix + "ReleaseNotes"));

            // The Vsix element declares its namespace as the default one, as Visual Studio's own feeds do.
            Assert.Contains("<Vsix xmlns=\"http://schemas.microsoft.com/developer/vsx-syndication-schema/2010\"", xml);
        }
    }

    public class Read
    {
        [Fact]
        public void ReadsWhatWriteWrites()
        {
            var xml = VsixAtomFeed.Write("uuid:1", "Contoso", "https://example.com/feeds/vs/vsix/atom.xml", DateTime.UtcNow, [Entry()]);

            var entry = Assert.Single(VsixAtomFeed.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)), new Uri("https://example.com/feeds/vs/vsix/atom.xml")));

            Assert.Equal("Contoso.HelloTools", entry.Manifest.Id);
            Assert.Equal("1.2.0", entry.Manifest.Version);
            Assert.Equal("Hello Tools", entry.Manifest.DisplayName);
            Assert.Equal("Says hello.", entry.Manifest.Description);
            Assert.Equal("Contoso", entry.Manifest.Publisher);
            Assert.Equal("https://github.com/contoso/hello-tools", entry.Manifest.MoreInfo);
            Assert.Equal(2, entry.Manifest.InstallationTargets.Count);
            Assert.Equal(new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc), entry.Updated);
            Assert.Equal(42, entry.Downloads);
            Assert.Equal(Entry().DownloadUrl, entry.DownloadUrl);
            Assert.Equal(Entry().IconUrl, entry.IconUrl);
            Assert.Null(entry.PreviewImageUrl);
        }

        [Fact]
        public void ResolvesRelativeUrlsAgainstTheFeed()
        {
            // As PrivateGalleryCreator writes them, with backslashes.
            var xml = """
                <feed xmlns="http://www.w3.org/2005/Atom">
                  <entry>
                    <id>SelectionHighlight..a14874d2</id>
                    <title type="text">Highlight all occurrences</title>
                    <link rel="icon" href="icons\SelectionHighlight..a14874d2.png" />
                    <content type="application/octet-stream" src="SelectionHighlight.vsix" />
                    <Vsix xmlns="http://schemas.microsoft.com/developer/vsx-syndication-schema/2010" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
                      <Id>SelectionHighlight..a14874d2</Id>
                      <Version>1.31</Version>
                      <DownloadCount xsi:nil="true" />
                    </Vsix>
                  </entry>
                </feed>
                """;

            var entry = Assert.Single(VsixAtomFeed.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)), new Uri("https://gallery.example.com/vs/feed.xml")));

            Assert.Equal("https://gallery.example.com/vs/SelectionHighlight.vsix", entry.DownloadUrl);
            Assert.Equal("https://gallery.example.com/vs/icons/SelectionHighlight..a14874d2.png", entry.IconUrl);
            Assert.Equal("Highlight all occurrences", entry.Manifest.DisplayName);
            Assert.Null(entry.Downloads);
            Assert.Empty(entry.Manifest.InstallationTargets);
        }

        [Fact]
        public void LeavesOutEntriesItCannotOffer()
        {
            var xml = """
                <feed xmlns="http://www.w3.org/2005/Atom">
                  <entry><id>NoFile</id><Vsix xmlns="http://schemas.microsoft.com/developer/vsx-syndication-schema/2010"><Version>1.0</Version></Vsix></entry>
                  <entry><id>NoVersion</id><content src="a.vsix" /></entry>
                  <entry><id>Bad Id</id><content src="b.vsix" /><Vsix xmlns="http://schemas.microsoft.com/developer/vsx-syndication-schema/2010"><Version>1.0</Version></Vsix></entry>
                  <entry><id>NotHttp</id><content src="file:///c:/c.vsix" /><Vsix xmlns="http://schemas.microsoft.com/developer/vsx-syndication-schema/2010"><Version>1.0</Version></Vsix></entry>
                  <entry><id>Good</id><content src="d.vsix" /><Vsix xmlns="http://schemas.microsoft.com/developer/vsx-syndication-schema/2010"><Version>1.0</Version></Vsix></entry>
                </feed>
                """;

            var entries = VsixAtomFeed.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)), new Uri("https://gallery.example.com/feed.xml"));

            Assert.Equal("Good", Assert.Single(entries).Manifest.Id);
        }

        [Fact]
        public void RejectsADocumentThatIsNotAnAtomFeed()
        {
            Assert.Throws<InvalidDataException>(() => VsixAtomFeed.Read(new MemoryStream(Encoding.UTF8.GetBytes("<html />")), new Uri("https://example.com/")));
        }
    }
}
