using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Pub;

namespace PaGetto.Core.Cargo;

/// <summary>
/// Reads a <c>.crate</c> file: a gzipped tar archive whose files are in a folder named
/// <c>{name}-{version}</c>, with the crate's <c>Cargo.toml</c> in it.
/// </summary>
public static class CargoCrateReader
{
    /// <summary>
    /// The largest crate accepted once decompressed. A small upload can decompress to a huge
    /// archive, which would otherwise keep the server busy reading it.
    /// </summary>
    public const long MaxUncompressedBytes = 1024L * 1024 * 1024;

    private const int MaxTextFileBytes = 1024 * 1024;

    private const string ManifestFileName = "Cargo.toml";
    private const string ReadmeFileName = "readme.md";

    /// <summary>
    /// Reads the whole crate, so a truncated or corrupt one is rejected.
    /// </summary>
    /// <param name="crate">The crate. It is read from its current position and left open.</param>
    /// <param name="name">The crate's name as published, which names its folder.</param>
    /// <param name="version">The crate's version, which names its folder.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The content of the crate's <c>README.md</c>, or null if it has none.</returns>
    /// <exception cref="InvalidCargoPackageException">The crate is not valid, or it is not the crate of this name and version.</exception>
    public static async Task<string> ReadAsync(Stream crate, string name, string version, CancellationToken cancellationToken)
    {
        var folder = $"{name}-{version}/";
        var hasManifest = false;
        string readme = null;

        try
        {
            await using var gzip = new GZipStream(crate, CompressionMode.Decompress, leaveOpen: true);
            await using var limited = new LimitedReadStream(
                gzip,
                MaxUncompressedBytes,
                () => new InvalidCargoPackageException(
                    $"The crate is larger than {MaxUncompressedBytes / 1024 / 1024} MiB when decompressed."));

            // The tar format is the same for every ecosystem, and this reader is lenient about headers.
            var tar = new PubTarReader(limited);

            while (await tar.GetNextEntryAsync(cancellationToken) is { } entry)
            {
                if (!entry.IsFile)
                {
                    continue;
                }

                var path = entry.Path.Replace('\\', '/');
                if (!path.StartsWith(folder, StringComparison.Ordinal))
                {
                    continue;
                }

                var fileName = path[folder.Length..];
                if (fileName == ManifestFileName)
                {
                    hasManifest = true;
                }
                else if (readme == null && fileName.Equals(ReadmeFileName, StringComparison.OrdinalIgnoreCase))
                {
                    readme = ReadText(await tar.ReadContentAsync(MaxTextFileBytes, cancellationToken));
                }
            }
        }
        catch (InvalidPubArchiveException e)
        {
            // Thrown by the tar reader.
            throw new InvalidCargoPackageException("The crate is not a valid tar archive.", e);
        }
        catch (InvalidDataException e)
        {
            // The data is not gzip, or the gzip stream is corrupt.
            throw new InvalidCargoPackageException("The crate is not a valid gzipped tar archive.", e);
        }

        if (!hasManifest)
        {
            throw new InvalidCargoPackageException($"The crate has no {folder}{ManifestFileName}.");
        }

        return readme;
    }

    private static string ReadText(byte[] content)
    {
        // The reader skips a byte order mark.
        using var reader = new StreamReader(new MemoryStream(content), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
