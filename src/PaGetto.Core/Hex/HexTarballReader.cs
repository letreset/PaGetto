using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Pub;

namespace PaGetto.Core.Hex;

/// <summary>
/// Reads a Hex package tarball: a tar archive with <c>VERSION</c> (<c>3</c>), <c>CHECKSUM</c> (the
/// inner checksum), <c>metadata.config</c> (Erlang terms) and <c>contents.tar.gz</c> (the files).
/// See: https://github.com/hexpm/specifications/blob/main/package_tarball.md
/// </summary>
public static class HexTarballReader
{
    /// <summary>
    /// The largest <c>contents.tar.gz</c> accepted, which is read into memory. Hex clients refuse
    /// packages over 16 MiB, so this leaves plenty of room.
    /// </summary>
    public const int MaxContentsBytes = 128 * 1024 * 1024;

    /// <summary>
    /// The largest contents accepted once decompressed. A small upload can decompress to a huge
    /// archive, which would otherwise keep the server busy reading it.
    /// </summary>
    public const long MaxUncompressedBytes = 1024L * 1024 * 1024;

    private const int MaxVersionBytes = 32;
    private const int MaxChecksumBytes = 128;
    private const int MaxMetadataBytes = 1024 * 1024;
    private const int MaxReadmeBytes = 1024 * 1024;

    private const string VersionFileName = "VERSION";
    private const string ChecksumFileName = "CHECKSUM";
    private const string MetadataFileName = "metadata.config";
    private const string ContentsFileName = "contents.tar.gz";
    private const string ReadmeFileName = "README.md";

    /// <summary>
    /// Reads and checks a tarball.
    /// </summary>
    /// <param name="tarball">The tarball. It is read from its current position and left open.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The package's manifest and the content of its <c>README.md</c>, or null if it has none.</returns>
    /// <exception cref="InvalidHexPackageException">The tarball is not a valid Hex package.</exception>
    public static async Task<(HexManifest Manifest, string Readme)> ReadAsync(Stream tarball, CancellationToken cancellationToken)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        try
        {
            var tar = new PubTarReader(tarball);
            while (await tar.GetNextEntryAsync(cancellationToken) is { } entry)
            {
                var maxBytes = entry.Path switch
                {
                    VersionFileName => MaxVersionBytes,
                    ChecksumFileName => MaxChecksumBytes,
                    MetadataFileName => MaxMetadataBytes,
                    ContentsFileName => MaxContentsBytes,
                    _ => 0,
                };

                if (!entry.IsFile || maxBytes == 0)
                {
                    continue;
                }

                if (!files.TryAdd(entry.Path, await tar.ReadContentAsync(maxBytes, cancellationToken)))
                {
                    throw new InvalidHexPackageException($"The package has more than one {entry.Path}.");
                }
            }
        }
        catch (InvalidPubArchiveException e)
        {
            // Thrown by the tar reader, also for a file over its size limit.
            throw new InvalidHexPackageException($"The package is not a valid tar archive: {e.Message}", e);
        }

        foreach (var name in new[] { VersionFileName, ChecksumFileName, MetadataFileName, ContentsFileName })
        {
            if (!files.ContainsKey(name))
            {
                throw new InvalidHexPackageException($"The package has no {name}.");
            }
        }

        var version = files[VersionFileName];
        if (Encoding.ASCII.GetString(version) != "3")
        {
            throw new InvalidHexPackageException("The package is not in version 3 of the Hex tarball format.");
        }

        var metadata = files[MetadataFileName];
        var contents = files[ContentsFileName];
        var innerChecksum = GetInnerChecksum(version, metadata, contents);

        byte[] expected;
        try
        {
            expected = Convert.FromHexString(Encoding.ASCII.GetString(files[ChecksumFileName]).Trim());
        }
        catch (FormatException)
        {
            expected = null;
        }

        if (expected == null || !CryptographicOperations.FixedTimeEquals(expected, innerChecksum))
        {
            throw new InvalidHexPackageException("The CHECKSUM of the package does not match its content.");
        }

        IReadOnlyList<object> terms;
        try
        {
            terms = ErlangTermText.Parse(metadata);
        }
        catch (FormatException e)
        {
            throw new InvalidHexPackageException($"The metadata.config of the package is not valid: {e.Message}", e);
        }

        var manifest = HexManifest.FromMetadata(terms, Convert.ToHexStringLower(innerChecksum));
        var readme = await ReadReadmeAsync(contents, cancellationToken);

        return (manifest, readme);
    }

    private static byte[] GetInnerChecksum(byte[] version, byte[] metadata, byte[] contents)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(version);
        hash.AppendData(metadata);
        hash.AppendData(contents);
        return hash.GetHashAndReset();
    }

    /// <summary>
    /// Reads the whole contents, so a corrupt archive is rejected, and returns its <c>README.md</c>.
    /// </summary>
    private static async Task<string> ReadReadmeAsync(byte[] contents, CancellationToken cancellationToken)
    {
        string readme = null;

        try
        {
            await using var gzip = new GZipStream(new MemoryStream(contents), CompressionMode.Decompress);
            await using var limited = new LimitedReadStream(
                gzip,
                MaxUncompressedBytes,
                () => new InvalidHexPackageException(
                    $"The contents of the package are larger than {MaxUncompressedBytes / 1024 / 1024} MiB when decompressed."));

            var tar = new PubTarReader(limited);
            while (await tar.GetNextEntryAsync(cancellationToken) is { } entry)
            {
                var path = entry.Path.Replace('\\', '/');
                if (path.StartsWith("./", StringComparison.Ordinal))
                {
                    path = path[2..];
                }

                if (readme == null && entry.IsFile && path.Equals(ReadmeFileName, StringComparison.OrdinalIgnoreCase))
                {
                    // The reader skips a byte order mark.
                    using var reader = new StreamReader(new MemoryStream(await tar.ReadContentAsync(MaxReadmeBytes, cancellationToken)), Encoding.UTF8);
                    readme = await reader.ReadToEndAsync(cancellationToken);
                }
            }
        }
        catch (InvalidPubArchiveException e)
        {
            throw new InvalidHexPackageException("The contents.tar.gz of the package is not a valid tar archive.", e);
        }
        catch (InvalidDataException e)
        {
            throw new InvalidHexPackageException("The contents.tar.gz of the package is not a valid gzipped tar archive.", e);
        }

        return readme;
    }
}
