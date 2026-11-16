using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PaGetto.Core.Artifacts;

namespace PaGetto.Core.Composer;

/// <summary>
/// Reads a package zip as Composer installs it: the package's files are at the root of the zip, or
/// in its only folder when the zip has nothing else (as <c>composer archive</c> and GitHub write
/// them), with <c>composer.json</c> among them.
/// </summary>
public static class ComposerArchiveReader
{
    private const long MaxComposerJsonBytes = 1024 * 1024;
    private const long MaxReadmeBytes = 1024 * 1024;

    private const string ComposerJsonFileName = "composer.json";
    private const string ReadmeFileName = "README.md";

    /// <summary>
    /// Reads and checks a package zip.
    /// </summary>
    /// <param name="zip">The zip. It must be seekable, and is left open.</param>
    /// <returns>The package's <c>composer.json</c> and readme.</returns>
    /// <exception cref="InvalidComposerPackageException">The zip is not a valid package zip.</exception>
    public static ComposerArchive Read(Stream zip)
    {
        try
        {
            using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true, Encoding.UTF8);

            var root = GetRoot(archive.Entries);
            JsonObject composerJson = null;
            string readme = null;

            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.StartsWith(root, StringComparison.Ordinal))
                {
                    continue;
                }

                var name = entry.FullName[root.Length..];
                if (name == ComposerJsonFileName)
                {
                    composerJson = ReadComposerJson(entry);
                }
                else if (readme == null && string.Equals(name, ReadmeFileName, StringComparison.OrdinalIgnoreCase) && entry.Length <= MaxReadmeBytes)
                {
                    readme = ReadText(entry, MaxReadmeBytes, () => new InvalidComposerPackageException("The readme is larger than its size in the zip."));
                }
            }

            if (composerJson == null)
            {
                throw new InvalidComposerPackageException(
                    $"The zip has no {ComposerJsonFileName} at its root{(root.Length == 0 ? "" : $" or in its folder {root}")}.");
            }

            return new ComposerArchive { ComposerJson = composerJson, Readme = readme };
        }
        catch (InvalidDataException e)
        {
            throw new InvalidComposerPackageException("The package is not a valid zip file.", e);
        }
    }

    /// <summary>
    /// The folder Composer installs the package from: the zip's only top-level folder when the zip
    /// has nothing else at its top level, or the root.
    /// </summary>
    private static string GetRoot(IReadOnlyCollection<ZipArchiveEntry> entries)
    {
        var topLevel = new HashSet<string>(StringComparer.Ordinal);
        var folders = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            var slash = entry.FullName.IndexOf('/');
            if (slash < 0)
            {
                topLevel.Add(entry.FullName);
                continue;
            }

            var folder = entry.FullName[..slash];
            topLevel.Add(folder);
            folders.Add(folder);
        }

        return topLevel.Count == 1 && folders.Count == 1 ? folders.Single() + "/" : "";
    }

    private static JsonObject ReadComposerJson(ZipArchiveEntry entry)
    {
        var text = ReadText(entry, MaxComposerJsonBytes, () => new InvalidComposerPackageException(
            $"The {ComposerJsonFileName} file is larger than {MaxComposerJsonBytes / 1024 / 1024} MiB."));

        try
        {
            return JsonNode.Parse(text) as JsonObject
                ?? throw new InvalidComposerPackageException($"The {ComposerJsonFileName} file is not a JSON object.");
        }
        catch (JsonException e)
        {
            throw new InvalidComposerPackageException($"The {ComposerJsonFileName} file is not valid JSON: {e.Message}", e);
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
