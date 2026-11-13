using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Artifacts;

namespace PaGetto.Core.Go;

/// <summary>
/// Reads a module zip as the go command downloads it: every file is in a folder named
/// <c>{module}@{version}/</c>, with the module's <c>go.mod</c> at its root. It checks the rules the
/// go command checks when it extracts a zip, so an upload the go command would reject is rejected.
/// See: https://go.dev/ref/mod#zip-files
/// </summary>
public static class GoModuleZipReader
{
    /// <summary>
    /// The largest module accepted once decompressed, the go command's limit.
    /// </summary>
    public const long MaxUncompressedBytes = 500L * 1024 * 1024;

    /// <summary>
    /// The largest <c>go.mod</c> file accepted, the go command's limit.
    /// </summary>
    public const long MaxGoModBytes = 16L * 1024 * 1024;

    private const long MaxReadmeBytes = 1024 * 1024;

    private const string GoModFileName = "go.mod";
    private const string ReadmeFileName = "README.md";

    /// <summary>
    /// Reads and checks a module zip.
    /// </summary>
    /// <param name="zip">The zip. It must be seekable, and is left open.</param>
    /// <param name="modulePath">The module path, which names the zip's folder.</param>
    /// <param name="version">The version, which names the zip's folder.</param>
    /// <returns>The module's <c>go.mod</c> and readme, each null if the zip has none.</returns>
    /// <exception cref="InvalidGoModuleException">The zip is not a valid zip of this module and version.</exception>
    public static GoModuleZip Read(Stream zip, string modulePath, string version)
    {
        var prefix = $"{modulePath}@{version}/";
        string goMod = null;
        string readme = null;
        long size = 0;
        var hasDirectoryEntries = false;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true, Encoding.UTF8);

            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.StartsWith(prefix, StringComparison.Ordinal))
                {
                    throw new InvalidGoModuleException($"The file {entry.FullName} of the zip is not in the folder {prefix}.");
                }

                var name = entry.FullName[prefix.Length..];
                if (name.Length == 0)
                {
                    hasDirectoryEntries = true;
                    continue;
                }

                var isDirectory = name.EndsWith('/');
                hasDirectoryEntries |= isDirectory;
                if (isDirectory)
                {
                    name = name[..^1];
                }

                if (!IsCleanPath(name))
                {
                    throw new InvalidGoModuleException($"The file {entry.FullName} of the zip has an invalid path.");
                }

                if (isDirectory)
                {
                    continue;
                }

                if (!names.Add(name))
                {
                    throw new InvalidGoModuleException($"The zip has the file {name} twice (the go command ignores case).");
                }

                size += entry.Length;
                if (size > MaxUncompressedBytes)
                {
                    throw new InvalidGoModuleException($"The module is larger than {MaxUncompressedBytes / 1024 / 1024} MiB when decompressed.");
                }

                // The go command leaves out go.mod files of folders, which belong to other modules.
                if (name == GoModFileName)
                {
                    goMod = ReadText(entry, MaxGoModBytes, () => new InvalidGoModuleException(
                        $"The go.mod file is larger than {MaxGoModBytes / 1024 / 1024} MiB."));
                }
                else if (string.Equals(name, GoModFileName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidGoModuleException($"The go.mod file must be named go.mod, not {name}.");
                }
                else if (readme == null && string.Equals(name, ReadmeFileName, StringComparison.OrdinalIgnoreCase) && entry.Length <= MaxReadmeBytes)
                {
                    readme = ReadText(entry, MaxReadmeBytes, () => new InvalidGoModuleException("The readme is larger than its size in the zip."));
                }
            }
        }
        catch (InvalidDataException e)
        {
            throw new InvalidGoModuleException("The module is not a valid zip file.", e);
        }

        return new GoModuleZip { GoMod = goMod, Readme = readme, HasDirectoryEntries = hasDirectoryEntries };
    }

    /// <summary>
    /// Writes a zip again without its folder entries, which the go command's zips never have.
    /// </summary>
    /// <param name="zip">A zip that <see cref="Read"/> accepted. It must be seekable, and is left open.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The zip without folder entries, in a temporary file.</returns>
    public static async Task<ArtifactFileContent> RemoveDirectoryEntriesAsync(Stream zip, CancellationToken cancellationToken)
    {
        zip.Position = 0;
        using var buffer = new MemoryStream();
        using (var source = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true, Encoding.UTF8))
        using (var target = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8))
        {
            foreach (var entry in source.Entries)
            {
                if (entry.FullName.EndsWith('/'))
                {
                    continue;
                }

                var copy = target.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                copy.LastWriteTime = entry.LastWriteTime;

                await using var from = entry.Open();
                await using var to = copy.Open();
                await from.CopyToAsync(to, cancellationToken);
            }
        }

        buffer.Position = 0;
        return await ArtifactFileContent.ReadAsync(buffer, long.MaxValue, cancellationToken);
    }

    /// <summary>
    /// Whether a path in the module is clean, like Go's <c>path.Clean</c> leaves it, and has no
    /// characters that file systems reject.
    /// </summary>
    private static bool IsCleanPath(string path)
    {
        foreach (var element in path.Split('/'))
        {
            if (element is "" or "." or "..")
            {
                return false;
            }
        }

        foreach (var c in path)
        {
            if (char.IsControl(c) || c is '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|')
            {
                return false;
            }
        }

        return true;
    }

    private static string ReadText(ZipArchiveEntry entry, long maxBytes, Func<Exception> createLimitException)
    {
        using var content = entry.Open();
        using var limited = new LimitedReadStream(content, maxBytes, createLimitException);

        // The text is kept as it is, so the go.mod served as the .mod file has the bytes of the zip's.
        using var reader = new StreamReader(limited, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false);
        return reader.ReadToEnd();
    }
}
