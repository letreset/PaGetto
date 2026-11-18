using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PaGetto.Core.Vsix;

/// <summary>
/// The Atom feed of a private gallery, which Visual Studio reads when the feed is added under
/// Tools &gt; Options &gt; Environment &gt; Extensions &gt; Additional Extension Galleries. Each entry is the
/// current version of an extension, with a <c>Vsix</c> element of the VSIX syndication schema that
/// Visual Studio compares with the installed version to offer updates.
/// See: https://learn.microsoft.com/visualstudio/extensibility/how-to-create-an-atom-feed-for-a-private-gallery
/// </summary>
public static class VsixAtomFeed
{
    public const string ContentType = "application/atom+xml; charset=utf-8";

    private static readonly XNamespace _atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace _vsix = "http://schemas.microsoft.com/developer/vsx-syndication-schema/2010";
    private static readonly XNamespace _xsi = "http://www.w3.org/2001/XMLSchema-instance";

    /// <summary>
    /// Writes a gallery feed.
    /// </summary>
    /// <param name="id">The feed's identifier, which stays the same, e.g. <c>uuid:{feed id}</c>.</param>
    /// <param name="title">The gallery's name.</param>
    /// <param name="selfUrl">The absolute URL of the feed.</param>
    /// <param name="updated">When the feed last changed.</param>
    /// <param name="entries">The extensions.</param>
    public static string Write(string id, string title, string selfUrl, DateTime updated, IEnumerable<VsixGalleryEntry> entries)
    {
        var feed = new XElement(
            _atom + "feed",
            new XElement(_atom + "title", new XAttribute("type", "text"), title),
            new XElement(_atom + "id", id),
            new XElement(_atom + "updated", FormatDate(updated)),
            new XElement(_atom + "link", new XAttribute("rel", "self"), new XAttribute("href", selfUrl)),
            entries.Select(WriteEntry));

        // Written to bytes, so the declaration says utf-8 rather than the utf-16 of a string.
        using var content = new MemoryStream();
        using (var writer = XmlWriter.Create(content, new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) }))
        {
            new XDocument(feed).Save(writer);
        }

        return Encoding.UTF8.GetString(content.ToArray());
    }

    /// <summary>
    /// Reads the entries of a gallery feed, such as a mirror's. Entries without an identifier, a
    /// version or a file are left out, and so are those of extensions this server can't store.
    /// </summary>
    /// <param name="feed">The feed's XML.</param>
    /// <param name="baseUrl">The feed's URL, which relative URLs are resolved against.</param>
    /// <exception cref="XmlException">The feed is not XML.</exception>
    /// <exception cref="InvalidDataException">The feed is not an Atom feed.</exception>
    public static IReadOnlyList<VsixGalleryEntry> Read(Stream feed, Uri baseUrl)
    {
        using var reader = XmlReader.Create(feed, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var root = XDocument.Load(reader).Root;
        if (root?.Name != _atom + "feed")
        {
            throw new InvalidDataException("The gallery is not an Atom feed.");
        }

        var entries = new List<VsixGalleryEntry>();
        foreach (var entry in root.Elements(_atom + "entry"))
        {
            var vsix = entry.Element(_vsix + "Vsix");
            var id = GetText(vsix, _vsix + "Id") ?? GetText(entry, _atom + "id");
            var downloadUrl = Resolve(baseUrl, (string)entry.Element(_atom + "content")?.Attribute("src"));
            if (!VsixExtensionId.IsValid(id) || !VsixVersion.TryParse(GetText(vsix, _vsix + "Version"), out var version) || downloadUrl == null)
            {
                continue;
            }

            var published = GetDate(entry, "published");
            var updated = GetDate(entry, "updated") ?? published ?? DateTime.UnixEpoch;

            entries.Add(new VsixGalleryEntry
            {
                Manifest = new VsixManifest
                {
                    Id = id,
                    Version = version.Text,
                    Publisher = GetText(entry.Element(_atom + "author"), _atom + "name"),
                    DisplayName = GetText(entry, _atom + "title") ?? id,
                    Description = GetText(entry, _atom + "summary"),
                    MoreInfo = GetText(vsix, _vsix + "MoreInfo"),
                    GettingStartedGuide = GetText(vsix, _vsix + "GettingStartedGuide"),
                    ReleaseNotes = GetText(vsix, _vsix + "ReleaseNotes"),
                    InstallationTargets = vsix?.Elements(_vsix + "Installations")
                        .Select(i => new VsixInstallationTarget
                        {
                            Id = GetText(i, _vsix + "Identifier"),
                            Version = GetText(i, _vsix + "VersionRange"),
                            ProductArchitecture = GetText(i, _vsix + "ProductArchitecture"),
                        })
                        .Where(t => t.Id != null)
                        .ToList() ?? [],
                },
                Published = published ?? updated,
                Updated = updated,
                Downloads = long.TryParse(GetText(vsix, _vsix + "DownloadCount"), NumberStyles.None, CultureInfo.InvariantCulture, out var downloads) ? downloads : null,
                DownloadUrl = downloadUrl,
                IconUrl = Resolve(baseUrl, GetLink(entry, "icon")),
                PreviewImageUrl = Resolve(baseUrl, GetLink(entry, "previewimage")),
            });
        }

        return entries;
    }

    private static XElement WriteEntry(VsixGalleryEntry entry)
    {
        var manifest = entry.Manifest;
        var moreInfo = VsixManifest.GetLink(manifest.MoreInfo);

        return new XElement(
            _atom + "entry",
            new XElement(_atom + "id", manifest.Id),
            new XElement(_atom + "title", new XAttribute("type", "text"), manifest.DisplayName ?? manifest.Id),
            new XElement(_atom + "summary", new XAttribute("type", "text"), manifest.Description ?? ""),
            new XElement(_atom + "published", FormatDate(entry.Published)),
            new XElement(_atom + "updated", FormatDate(entry.Updated)),
            new XElement(_atom + "author", new XElement(_atom + "name", manifest.Publisher ?? "")),
            moreInfo == null ? null : new XElement(_atom + "link", new XAttribute("rel", "alternate"), new XAttribute("href", moreInfo)),
            entry.IconUrl == null ? null : new XElement(_atom + "link", new XAttribute("rel", "icon"), new XAttribute("href", entry.IconUrl)),
            entry.PreviewImageUrl == null ? null : new XElement(_atom + "link", new XAttribute("rel", "previewimage"), new XAttribute("href", entry.PreviewImageUrl)),
            new XElement(_atom + "content", new XAttribute("type", "application/octet-stream"), new XAttribute("src", entry.DownloadUrl)),
            new XElement(
                _vsix + "Vsix",
                new XAttribute("xmlns", _vsix.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "xsi", _xsi.NamespaceName),
                new XElement(_vsix + "Id", manifest.Id),
                new XElement(_vsix + "Version", manifest.Version),
                new XElement(_vsix + "References"),
                Nil("Rating"),
                Nil("RatingCount"),
                entry.Downloads is { } downloads ? new XElement(_vsix + "DownloadCount", downloads) : Nil("DownloadCount"),
                manifest.InstallationTargets.Select(t => new XElement(
                    _vsix + "Installations",
                    new XElement(_vsix + "Identifier", t.Id),
                    new XElement(_vsix + "VersionRange", t.Version ?? ""),
                    t.ProductArchitecture == null ? null : new XElement(_vsix + "ProductArchitecture", t.ProductArchitecture))),
                moreInfo == null ? null : new XElement(_vsix + "MoreInfo", moreInfo),
                VsixManifest.GetLink(manifest.GettingStartedGuide) is { } guide ? new XElement(_vsix + "GettingStartedGuide", guide) : null,
                VsixManifest.GetLink(manifest.ReleaseNotes) is { } releaseNotes ? new XElement(_vsix + "ReleaseNotes", releaseNotes) : null));
    }

    private static XElement Nil(string name)
    {
        return new XElement(_vsix + name, new XAttribute(_xsi + "nil", "true"));
    }

    private static string FormatDate(DateTime date)
    {
        return date.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private static DateTime? GetDate(XElement entry, string name)
    {
        return DateTime.TryParse(GetText(entry, _atom + name), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : null;
    }

    private static string GetLink(XElement entry, string rel)
    {
        return (string)entry.Elements(_atom + "link").FirstOrDefault(l => string.Equals((string)l.Attribute("rel"), rel, StringComparison.OrdinalIgnoreCase))?.Attribute("href");
    }

    private static string GetText(XElement parent, XName name)
    {
        var value = parent?.Element(name)?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>
    /// An absolute http or https URL, relative ones resolved against the feed's URL (with the
    /// backslashes some gallery tools write as slashes), or null.
    /// </summary>
    private static string Resolve(Uri baseUrl, string url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(baseUrl, url.Trim().Replace('\\', '/'), out var resolved))
        {
            return null;
        }

        return resolved.Scheme == Uri.UriSchemeHttp || resolved.Scheme == Uri.UriSchemeHttps ? resolved.AbsoluteUri : null;
    }
}
