using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Pub;

namespace PaGetto.Core.Cran;

/// <summary>
/// Reads a source package as <c>R CMD build</c> writes it: a gzipped tar archive whose files are in
/// a folder named after the package, with the package's <c>DESCRIPTION</c> file in it.
/// </summary>
public static class CranArchiveReader
{
    /// <summary>
    /// The largest package accepted once decompressed. A small upload can decompress to a huge
    /// archive, which would otherwise keep the server busy reading it.
    /// </summary>
    public const long MaxUncompressedBytes = 1024L * 1024 * 1024;

    private const int MaxTextFileBytes = 1024 * 1024;

    private const string DescriptionFileName = "DESCRIPTION";
    private const string ReadmeFileName = "README.md";

    /// <summary>
    /// Reads the whole package, so a truncated or corrupt one is rejected.
    /// </summary>
    /// <param name="archive">The package. It is read from its current position and left open.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The package's <c>DESCRIPTION</c> fields and readme.</returns>
    /// <exception cref="InvalidCranPackageException">
    /// The package is not valid: it is not a gzipped tar archive, it has no <c>{name}/DESCRIPTION</c>
    /// file, or that file lacks a valid name and version or names another folder.
    /// </exception>
    public static async Task<CranArchive> ReadAsync(Stream archive, CancellationToken cancellationToken)
    {
        string folder = null;
        byte[] description = null;
        var readmes = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            await using var gzip = new GZipStream(archive, CompressionMode.Decompress, leaveOpen: true);
            await using var limited = new LimitedReadStream(
                gzip,
                MaxUncompressedBytes,
                () => new InvalidCranPackageException(
                    $"The package is larger than {MaxUncompressedBytes / 1024 / 1024} MiB when decompressed."));

            // The tar format is the same for every ecosystem, and this reader is lenient about headers.
            var tar = new PubTarReader(limited);

            while (await tar.GetNextEntryAsync(cancellationToken) is { } entry)
            {
                if (!entry.IsFile)
                {
                    continue;
                }

                var path = entry.Path.Replace('\\', '/');
                if (path.StartsWith("./", StringComparison.Ordinal))
                {
                    path = path[2..];
                }

                // Only the files at the top of the package's folder: a package may have other
                // DESCRIPTION files deeper down, e.g. in inst/.
                var slash = path.IndexOf('/');
                if (slash <= 0 || path.IndexOf('/', slash + 1) >= 0)
                {
                    continue;
                }

                var fileName = path[(slash + 1)..];
                if (fileName == DescriptionFileName && description == null)
                {
                    folder = path[..slash];
                    description = await tar.ReadContentAsync(MaxTextFileBytes, cancellationToken);
                }
                else if (string.Equals(fileName, ReadmeFileName, StringComparison.OrdinalIgnoreCase) && entry.Size <= MaxTextFileBytes)
                {
                    readmes.TryAdd(path[..slash], ReadText(await tar.ReadContentAsync(MaxTextFileBytes, cancellationToken)));
                }
            }
        }
        catch (InvalidPubArchiveException e)
        {
            // Thrown by the tar reader.
            throw new InvalidCranPackageException("The package is not a valid tar archive.", e);
        }
        catch (InvalidDataException e)
        {
            // The data is not gzip, or the gzip stream is corrupt.
            throw new InvalidCranPackageException("The package is not a valid gzipped tar archive (.tar.gz).", e);
        }

        if (description == null)
        {
            throw new InvalidCranPackageException("The package has no DESCRIPTION file in its folder, as R CMD build writes it: {name}/DESCRIPTION.");
        }

        IReadOnlyList<KeyValuePair<string, string>> fields;
        try
        {
            fields = CranDcf.ReadRecord(ReadText(description));
        }
        catch (InvalidDataException e)
        {
            throw new InvalidCranPackageException($"The DESCRIPTION file is not valid: {e.Message}", e);
        }

        var name = CranDcf.GetValue(fields, "Package");
        if (!CranPackageName.IsValid(name))
        {
            throw new InvalidCranPackageException(
                $"The DESCRIPTION file has {(name == null ? "no Package field" : $"the package name '{name}', which is not valid")}: " +
                "a name has letters, digits and dots, at least two characters, starts with a letter and doesn't end with a dot.");
        }

        if (folder != name)
        {
            throw new InvalidCranPackageException($"The DESCRIPTION file of the package {name} is in the folder {folder}, not {name}.");
        }

        var version = CranDcf.GetValue(fields, "Version");
        if (!CranVersion.TryParse(version, out var parsedVersion))
        {
            throw new InvalidCranPackageException(
                $"The DESCRIPTION file has {(version == null ? "no Version field" : $"the version '{version}', which is not valid")}: " +
                "a version is two or more numbers separated by '.' or '-', such as 1.0.2 or 0.9-12.");
        }

        return new CranArchive
        {
            Name = name,
            Version = parsedVersion,
            Description = fields,
            Readme = readmes.GetValueOrDefault(folder),
        };
    }

    /// <summary>
    /// Reads a text file as UTF-8, or as Latin-1 when it is not valid UTF-8 (the other encoding R
    /// packages commonly declare in <c>DESCRIPTION</c>).
    /// </summary>
    private static string ReadText(byte[] content)
    {
        var bytes = content.AsSpan();
        if (bytes.StartsWith(Encoding.UTF8.Preamble))
        {
            bytes = bytes[Encoding.UTF8.Preamble.Length..];
        }

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }
}
