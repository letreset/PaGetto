using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using PaGetto.Core.Artifacts;

namespace PaGetto.Core.Swift;

/// <summary>
/// Reads a source archive as Swift Package Manager unpacks it: a zip with one folder at its top
/// level, which holds the package with its <c>Package.swift</c>. Swift Package Manager strips that
/// folder and fails when the archive has more than one, and <c>swift package-registry publish</c>
/// writes archives this way.
/// </summary>
public static class SwiftArchiveReader
{
    private const long MaxManifestBytes = 1024 * 1024;
    private const long MaxReadmeBytes = 1024 * 1024;

    private const string ReadmeFileName = "README.md";

    /// <summary>
    /// Reads and checks a source archive.
    /// </summary>
    /// <param name="zip">The zip. It must be seekable, and is left open.</param>
    /// <returns>The package's manifests and readme.</returns>
    /// <exception cref="InvalidSwiftPackageException">The zip is not a valid source archive.</exception>
    public static SwiftArchive Read(Stream zip)
    {
        try
        {
            using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true, Encoding.UTF8);

            var folders = archive.Entries
                .Where(e => e.FullName.Contains('/'))
                .Select(e => e.FullName[..e.FullName.IndexOf('/')])
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (folders.Count != 1)
            {
                throw new InvalidSwiftPackageException(
                    $"The source archive must have one folder at its top level, with the package in it, but it has {folders.Count}. " +
                    "swift package-registry publish writes archives this way.");
            }

            var root = folders[0] + "/";
            var manifests = new Dictionary<string, string>(StringComparer.Ordinal);
            string readme = null;

            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.StartsWith(root, StringComparison.Ordinal))
                {
                    continue;
                }

                var name = entry.FullName[root.Length..];
                if (SwiftManifest.IsManifestFileName(name))
                {
                    manifests[name] = ReadText(entry, MaxManifestBytes, () => new InvalidSwiftPackageException(
                        $"The manifest {name} is larger than {MaxManifestBytes / 1024 / 1024} MiB."));
                }
                else if (readme == null && string.Equals(name, ReadmeFileName, StringComparison.OrdinalIgnoreCase) && entry.Length <= MaxReadmeBytes)
                {
                    readme = ReadText(entry, MaxReadmeBytes, () => new InvalidSwiftPackageException("The readme is larger than its size in the zip."));
                }
            }

            if (!manifests.ContainsKey(SwiftManifest.ManifestFileName))
            {
                throw new InvalidSwiftPackageException($"The source archive has no {SwiftManifest.ManifestFileName} in its folder {root}.");
            }

            return new SwiftArchive { Manifests = manifests, Readme = readme };
        }
        catch (InvalidDataException e)
        {
            throw new InvalidSwiftPackageException("The source archive is not a valid zip file.", e);
        }
    }

    private static string ReadText(ZipArchiveEntry entry, long maxBytes, Func<Exception> createLimitException)
    {
        using var content = entry.Open();
        using var limited = new LimitedReadStream(content, maxBytes, createLimitException);
        using var reader = new StreamReader(limited, new UTF8Encoding(false));
        return reader.ReadToEnd();
    }
}
