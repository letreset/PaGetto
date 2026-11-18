using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using PaGetto.Core.Artifacts;

namespace PaGetto.Core.Vsix;

/// <summary>
/// Reads a VSIX file: a zip (an Open Packaging Conventions package) with
/// <c>extension.vsixmanifest</c> and <c>[Content_Types].xml</c> at its root. The manifest must be
/// in the format of Visual Studio 2012 and later (version 2.0.0) and target Visual Studio; VS Code
/// extensions, whose manifest targets <c>Microsoft.VisualStudio.Code</c>, are refused.
/// See: https://learn.microsoft.com/visualstudio/extensibility/vsix-extension-schema-2-0-reference
/// </summary>
public static class VsixArchiveReader
{
    public const string ManifestFileName = "extension.vsixmanifest";
    public const string VsCodeInstallationTarget = "Microsoft.VisualStudio.Code";

    private const string ContentTypesFileName = "[Content_Types].xml";
    private const long MaxManifestBytes = 1024 * 1024;
    private const long MaxImageBytes = 4 * 1024 * 1024;

    // The dependency on the .NET Framework that the VSIX project adds; it is not an extension.
    private const string FrameworkDependency = "Microsoft.Framework.NDP";

    private static readonly XNamespace _namespace = "http://schemas.microsoft.com/developer/vsx-schema/2011";
    private static readonly XNamespace _version1Namespace = "http://schemas.microsoft.com/developer/vsx-schema/2010";

    private static readonly Dictionary<string, string> _imageContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".bmp"] = "image/bmp",
        [".ico"] = "image/x-icon",
    };

    /// <summary>
    /// Reads and checks a VSIX file.
    /// </summary>
    /// <param name="vsix">The file. It must be seekable, and is left open.</param>
    /// <returns>The extension's manifest. Its icon and preview image are kept only when the file has them as images this server serves.</returns>
    /// <exception cref="InvalidVsixPackageException">The file is not a valid VSIX file of a Visual Studio extension.</exception>
    public static VsixManifest Read(Stream vsix)
    {
        try
        {
            using var archive = new ZipArchive(vsix, ZipArchiveMode.Read, leaveOpen: true);

            var manifestEntry = FindEntry(archive, ManifestFileName)
                ?? throw new InvalidVsixPackageException($"The VSIX file has no {ManifestFileName} at its root.");

            if (FindEntry(archive, ContentTypesFileName) == null)
            {
                throw new InvalidVsixPackageException(
                    $"The VSIX file has no {ContentTypesFileName} at its root, which Visual Studio needs to install it. Build it with the VSIX project of the Visual Studio SDK.");
            }

            return ReadManifest(archive, manifestEntry);
        }
        catch (InvalidDataException e)
        {
            throw new InvalidVsixPackageException("The package is not a valid VSIX (zip) file.", e);
        }
    }

    /// <summary>
    /// Reads an image of a VSIX file, at a path its manifest gives.
    /// </summary>
    /// <param name="vsix">The file. It must be seekable, and is left open.</param>
    /// <param name="path">The image's path in the file.</param>
    /// <returns>The image, or null if the file has no such image.</returns>
    public static VsixImage ReadImage(Stream vsix, string path)
    {
        var contentType = GetImageContentType(path);
        if (contentType == null)
        {
            return null;
        }

        try
        {
            using var archive = new ZipArchive(vsix, ZipArchiveMode.Read, leaveOpen: true);
            var entry = FindEntry(archive, path);
            if (entry == null || entry.Length > MaxImageBytes)
            {
                return null;
            }

            using var content = entry.Open();
            using var limited = new LimitedReadStream(content, MaxImageBytes, () => new InvalidDataException("The image is larger than its size in the zip."));
            using var image = new MemoryStream();
            limited.CopyTo(image);

            return new VsixImage { Content = image.ToArray(), ContentType = contentType };
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>
    /// The media type of an image by its file extension, or null if it is not an image this server serves.
    /// </summary>
    public static string GetImageContentType(string path)
    {
        return path != null && _imageContentTypes.TryGetValue(Path.GetExtension(path), out var contentType) ? contentType : null;
    }

    private static VsixManifest ReadManifest(ZipArchive archive, ZipArchiveEntry entry)
    {
        XDocument document;
        try
        {
            using var content = entry.Open();
            using var limited = new LimitedReadStream(content, MaxManifestBytes, () => new InvalidVsixPackageException(
                $"The {ManifestFileName} file is larger than {MaxManifestBytes / 1024 / 1024} MiB."));
            using var reader = XmlReader.Create(limited, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document = XDocument.Load(reader);
        }
        catch (XmlException e)
        {
            throw new InvalidVsixPackageException($"The {ManifestFileName} file is not valid XML: {e.Message}", e);
        }

        var root = document.Root;
        if (root?.Name == _version1Namespace + "Vsix")
        {
            throw new InvalidVsixPackageException(
                $"The {ManifestFileName} file has the format of Visual Studio 2010 (version 1.0.0). Visual Studio 2012 and later read version 2.0.0, which the VSIX project of the Visual Studio SDK writes.");
        }

        if (root?.Name != _namespace + "PackageManifest")
        {
            throw new InvalidVsixPackageException($"The {ManifestFileName} file is not a VSIX manifest: its root is not a PackageManifest of {_namespace.NamespaceName}.");
        }

        var metadata = root.Element(_namespace + "Metadata");
        var identity = metadata?.Element(_namespace + "Identity")
            ?? throw new InvalidVsixPackageException($"The {ManifestFileName} file has no Metadata/Identity element.");

        var id = (string)identity.Attribute("Id");
        if (!VsixExtensionId.IsValid(id))
        {
            throw new InvalidVsixPackageException(string.IsNullOrEmpty(id)
                ? "The Identity of the manifest has no Id."
                : $"The Id {id} of the manifest is not valid: it must have letters, digits, dots, dashes and underscores, start and end with a letter or digit, and have at most {VsixExtensionId.MaxLength} characters.");
        }

        var versionText = (string)identity.Attribute("Version");
        if (!VsixVersion.TryParse(versionText, out var version))
        {
            throw new InvalidVsixPackageException($"The Version {versionText} of the manifest is not valid: it must be two to four numbers separated by dots, such as 1.0.0.");
        }

        var displayName = GetText(metadata, "DisplayName")
            ?? throw new InvalidVsixPackageException("The manifest has no DisplayName.");

        var targets = root.Element(_namespace + "Installation")?.Elements(_namespace + "InstallationTarget")
            .Select(t => new VsixInstallationTarget
            {
                Id = (string)t.Attribute("Id"),
                Version = (string)t.Attribute("Version"),
                ProductArchitecture = GetText(t, "ProductArchitecture"),
            })
            .Where(t => !string.IsNullOrEmpty(t.Id))
            .ToList() ?? [];

        if (targets.Count == 0)
        {
            throw new InvalidVsixPackageException("The manifest has no Installation/InstallationTarget, so Visual Studio wouldn't know which products the extension installs into.");
        }

        if (targets.Exists(t => string.Equals(t.Id, VsCodeInstallationTarget, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidVsixPackageException($"{id} is a VS Code extension (it targets {VsCodeInstallationTarget}). VSIX feeds host Visual Studio extensions.");
        }

        var dependencies = root.Element(_namespace + "Dependencies")?.Elements(_namespace + "Dependency")
            .Select(d => new VsixDependency
            {
                Id = (string)d.Attribute("Id"),
                DisplayName = (string)d.Attribute("DisplayName"),
                Version = (string)d.Attribute("Version"),
            })
            .Where(d => !string.IsNullOrEmpty(d.Id) && !string.Equals(d.Id, FrameworkDependency, StringComparison.OrdinalIgnoreCase))
            .ToList() ?? [];

        var tags = (GetText(metadata, "Tags") ?? "")
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new VsixManifest
        {
            Id = id,
            Version = version.Text,
            Publisher = (string)identity.Attribute("Publisher"),
            Language = (string)identity.Attribute("Language"),
            DisplayName = displayName,
            Description = GetText(metadata, "Description"),
            MoreInfo = GetText(metadata, "MoreInfo"),
            GettingStartedGuide = GetText(metadata, "GettingStartedGuide"),
            ReleaseNotes = GetText(metadata, "ReleaseNotes"),
            Icon = FindImage(archive, GetText(metadata, "Icon")),
            PreviewImage = FindImage(archive, GetText(metadata, "PreviewImage")),
            Tags = tags,
            Preview = bool.TryParse(GetText(metadata, "Preview"), out var preview) && preview,
            InstallationTargets = targets,
            Dependencies = dependencies,
        };
    }

    /// <summary>
    /// The path of an image when the file has it and this server serves its type, or null.
    /// </summary>
    private static string FindImage(ZipArchive archive, string path)
    {
        return GetImageContentType(path) != null && FindEntry(archive, path) != null ? path : null;
    }

    /// <summary>
    /// Finds a file by its path in the manifest, which may use backslashes. The names of the parts
    /// of a package are matched regardless of case, and may be escaped in the zip (a space as <c>%20</c>).
    /// </summary>
    private static ZipArchiveEntry FindEntry(ZipArchive archive, string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        if (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return archive.Entries.FirstOrDefault(e => string.Equals(e.FullName, normalized, StringComparison.OrdinalIgnoreCase))
            ?? archive.Entries.FirstOrDefault(e => string.Equals(Uri.UnescapeDataString(e.FullName), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetText(XElement parent, string name)
    {
        var value = parent?.Element(_namespace + name)?.Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
