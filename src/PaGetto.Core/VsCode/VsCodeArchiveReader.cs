using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Npm;

namespace PaGetto.Core.VsCode;

/// <summary>
/// Reads a VSIX file of a VS Code extension, as <c>vsce package</c> writes it: a zip with
/// <c>extension.vsixmanifest</c> at its root, whose installation target is
/// <c>Microsoft.VisualStudio.Code</c>, and the extension under <c>extension/</c>, with its
/// <c>package.json</c>. The manifest lists the files the gallery serves (its assets).
/// See: https://github.com/microsoft/vscode-vsce
/// </summary>
public static class VsCodeArchiveReader
{
    public const string ManifestFileName = "extension.vsixmanifest";

    /// <summary>
    /// The most an asset served from a VSIX file may have.
    /// </summary>
    public const long MaxAssetBytes = 16L * 1024 * 1024;

    private const long MaxManifestBytes = 1024 * 1024;

    private static readonly XNamespace _namespace = "http://schemas.microsoft.com/developer/vsx-schema/2011";

    private static readonly Dictionary<string, string> _contentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".svg"] = "image/svg+xml",
        [".json"] = "application/json",
        [".md"] = "text/markdown; charset=utf-8",
        [".txt"] = "text/plain; charset=utf-8",
        [".vsixmanifest"] = "application/xml",
    };

    /// <summary>
    /// Reads and checks a VSIX file.
    /// </summary>
    /// <param name="vsix">The file. It must be seekable, and is left open.</param>
    /// <returns>The manifest of the file, with the assets the file has.</returns>
    /// <exception cref="InvalidVsCodePackageException">The file is not a valid VSIX file of a VS Code extension.</exception>
    public static VsCodeManifest Read(Stream vsix)
    {
        try
        {
            using var archive = new ZipArchive(vsix, ZipArchiveMode.Read, leaveOpen: true);

            var manifestEntry = FindEntry(archive, ManifestFileName)
                ?? throw new InvalidVsCodePackageException($"The VSIX file has no {ManifestFileName} at its root. Package the extension with vsce package.");

            return ReadManifest(archive, manifestEntry);
        }
        catch (InvalidDataException e)
        {
            throw new InvalidVsCodePackageException("The package is not a valid VSIX (zip) file.", e);
        }
    }

    /// <summary>
    /// Reads a file of a VSIX file, such as an asset its manifest lists.
    /// </summary>
    /// <param name="vsix">The file. It must be seekable, and is left open.</param>
    /// <param name="path">The path in the file.</param>
    /// <returns>The content, or null if the file has no such file or it is larger than <see cref="MaxAssetBytes"/>.</returns>
    public static byte[] ReadFileOrNull(Stream vsix, string path)
    {
        try
        {
            using var archive = new ZipArchive(vsix, ZipArchiveMode.Read, leaveOpen: true);
            var entry = FindEntry(archive, path);
            if (entry == null || entry.Length > MaxAssetBytes)
            {
                return null;
            }

            using var content = entry.Open();
            using var limited = new LimitedReadStream(content, MaxAssetBytes, () => new InvalidDataException("The file is larger than its size in the zip."));
            using var copy = new MemoryStream();
            limited.CopyTo(copy);

            return copy.ToArray();
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// The media type an asset is served with, by its path.
    /// </summary>
    public static string GetContentType(string path)
    {
        return path != null && _contentTypes.TryGetValue(Path.GetExtension(path), out var contentType)
            ? contentType
            : "application/octet-stream";
    }

    private static VsCodeManifest ReadManifest(ZipArchive archive, ZipArchiveEntry entry)
    {
        XDocument document;
        try
        {
            using var content = entry.Open();
            using var limited = new LimitedReadStream(content, MaxManifestBytes, () => new InvalidVsCodePackageException(
                $"The {ManifestFileName} file is larger than {MaxManifestBytes / 1024 / 1024} MiB."));
            using var reader = XmlReader.Create(limited, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document = XDocument.Load(reader);
        }
        catch (XmlException e)
        {
            throw new InvalidVsCodePackageException($"The {ManifestFileName} file is not valid XML: {e.Message}", e);
        }

        var root = document.Root;
        if (root?.Name != _namespace + "PackageManifest")
        {
            throw new InvalidVsCodePackageException($"The {ManifestFileName} file is not a VSIX manifest: its root is not a PackageManifest of {_namespace.NamespaceName}.");
        }

        var targets = root.Element(_namespace + "Installation")?.Elements(_namespace + "InstallationTarget")
            .Select(t => (string)t.Attribute("Id"))
            .ToList() ?? [];
        if (!targets.Exists(t => string.Equals(t, VsCodeGalleryApi.InstallationTarget, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidVsCodePackageException(
                $"The extension does not target {VsCodeGalleryApi.InstallationTarget}, so it is not a VS Code extension. Visual Studio extensions go to a VSIX feed.");
        }

        var metadata = root.Element(_namespace + "Metadata");
        var identity = metadata?.Element(_namespace + "Identity")
            ?? throw new InvalidVsCodePackageException($"The {ManifestFileName} file has no Metadata/Identity element.");

        var publisher = (string)identity.Attribute("Publisher");
        var name = (string)identity.Attribute("Id");
        if (VsCodeExtensionId.Create(publisher, name) == null)
        {
            throw new InvalidVsCodePackageException(
                $"The extension {publisher}.{name} has no valid identifier: its publisher and name must have letters, digits, dashes and underscores, start with a letter or digit, and have at most {VsCodeExtensionId.MaxLength} characters together.");
        }

        var version = (string)identity.Attribute("Version");
        if (!NpmVersion.TryParse(version, out _))
        {
            throw new InvalidVsCodePackageException($"The Version {version} of the manifest is not a semantic version, such as 1.0.0.");
        }

        var platformText = (string)identity.Attribute("TargetPlatform");
        if (!VsCodeTargetPlatform.TryParse(platformText, out var platform))
        {
            throw new InvalidVsCodePackageException($"The TargetPlatform {platformText} of the manifest is not a platform VS Code knows, such as linux-x64.");
        }

        var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in root.Element(_namespace + "Assets")?.Elements(_namespace + "Asset") ?? [])
        {
            var type = (string)asset.Attribute("Type");
            var path = (string)asset.Attribute("Path");
            if (!string.IsNullOrEmpty(type) && !string.IsNullOrEmpty(path) && FindEntry(archive, path) != null)
            {
                assets.TryAdd(type, path.Replace('\\', '/'));
            }
        }

        if (!assets.ContainsKey(VsCodeGalleryApi.ManifestAsset))
        {
            throw new InvalidVsCodePackageException(
                $"The manifest has no {VsCodeGalleryApi.ManifestAsset} asset in the file, the extension's package.json. Package the extension with vsce package.");
        }

        var properties = (metadata.Element(_namespace + "Properties")?.Elements(_namespace + "Property") ?? [])
            .Select(p => new VsCodeGalleryProperty { Key = (string)p.Attribute("Id"), Value = (string)p.Attribute("Value") ?? "" })
            .Where(p => !string.IsNullOrEmpty(p.Key))
            .ToList();

        return new VsCodeManifest
        {
            Publisher = publisher,
            Name = name,
            Version = version,
            TargetPlatform = platform,
            DisplayName = GetText(metadata, "DisplayName") ?? name,
            Description = GetText(metadata, "Description"),
            Tags = Split(GetText(metadata, "Tags")),
            Categories = Split(GetText(metadata, "Categories")),
            GalleryFlags = GetText(metadata, "GalleryFlags"),
            Properties = properties,
            Assets = assets,
        };
    }

    private static List<string> Split(string text)
    {
        return (text ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Finds a file by its path in the manifest. The names of the parts of a package are matched
    /// regardless of case, and may be escaped in the zip (a space as <c>%20</c>).
    /// </summary>
    private static ZipArchiveEntry FindEntry(ZipArchive archive, string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');

        return archive.Entries.FirstOrDefault(e => string.Equals(e.FullName, normalized, StringComparison.OrdinalIgnoreCase))
            ?? archive.Entries.FirstOrDefault(e => string.Equals(Uri.UnescapeDataString(e.FullName), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetText(XElement parent, string name)
    {
        var value = parent?.Element(_namespace + name)?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
