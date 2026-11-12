using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Pub;

namespace PaGetto.Core.PyPI;

/// <summary>
/// Reads the core metadata of a distribution: the <c>{name}-{version}.dist-info/METADATA</c> file of a
/// wheel (a zip file), or the <c>{name}-{version}/PKG-INFO</c> file of an sdist (a gzipped tar
/// file, or a zip file for older ones).
/// </summary>
public static class PyPIArchiveReader
{
    /// <summary>
    /// How much of a gzipped sdist is decompressed while looking for its metadata. A small upload
    /// can decompress to a huge archive, which would otherwise keep the server busy reading it.
    /// </summary>
    public const long MaxUncompressedBytes = 1024L * 1024 * 1024;

    private const int MaxMetadataBytes = 16 * 1024 * 1024;

    private const string WheelMetadataFileName = "METADATA";
    private const string SdistMetadataFileName = "PKG-INFO";

    /// <summary>
    /// Reads the core metadata of a file, and checks that it is the metadata of the project and
    /// version in the file's name.
    /// </summary>
    /// <param name="content">The file. It must be seekable; it is read from the start and left open.</param>
    /// <param name="file">The file's name.</param>
    /// <param name="projectName">The project the file belongs to.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <exception cref="InvalidPyPIPackageException">The file is not a valid distribution of the project.</exception>
    public static async Task<PyPIArchive> ReadAsync(Stream content, PyPIFileName file, string projectName, CancellationToken cancellationToken)
    {
        var metadataFileName = file.IsWheel ? WheelMetadataFileName : SdistMetadataFileName;

        byte[] metadata;
        try
        {
            content.Position = 0;
            metadata = file.Extension == ".tar.gz"
                ? await ReadFromTarAsync(content, cancellationToken)
                : ReadFromZip(content, file.IsWheel, projectName);
        }
        catch (InvalidPubArchiveException e)
        {
            // Thrown by the tar reader.
            throw new InvalidPyPIPackageException($"{file.FileName} is not a valid tar archive: {e.Message}", e);
        }
        catch (InvalidDataException e)
        {
            // The data is not gzip or zip, or it is corrupt.
            throw new InvalidPyPIPackageException($"{file.FileName} is not a valid {file.Extension} archive.", e);
        }
        finally
        {
            content.Position = 0;
        }

        if (metadata == null)
        {
            throw new InvalidPyPIPackageException(file.IsWheel
                ? $"{file.FileName} has no {metadataFileName} file in a .dist-info folder."
                : $"{file.FileName} has no {metadataFileName} file in its top folder.");
        }

        var manifest = PyPICoreMetadata.Read(Encoding.UTF8.GetString(metadata), out var readme);

        var name = PyPICoreMetadata.GetString(manifest, "name");
        if (name == null || PyPIPackageName.Normalize(name) != PyPIPackageName.Normalize(projectName))
        {
            throw new InvalidPyPIPackageException($"The {metadataFileName} of {file.FileName} is for the project '{name}', not '{projectName}'.");
        }

        var version = PyPICoreMetadata.GetString(manifest, "version");
        if (PyPIVersion.NormalizeOrNull(version) != file.Version)
        {
            throw new InvalidPyPIPackageException($"The {metadataFileName} of {file.FileName} is for version '{version}', not '{file.Version}'.");
        }

        return new PyPIArchive
        {
            Metadata = metadata,
            Manifest = manifest,
            Readme = readme,
        };
    }

    private static byte[] ReadFromZip(Stream content, bool isWheel, string projectName)
    {
        using var zip = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);

        // The metadata is one folder down: in the .dist-info folder of a wheel, in the top folder of an sdist.
        var candidates = zip.Entries
            .Select(e => (Entry: e, Path: e.FullName.Replace('\\', '/').Split('/')))
            .Where(c => c.Path.Length == 2 && (isWheel
                ? c.Path[0].EndsWith(".dist-info", StringComparison.OrdinalIgnoreCase) && c.Path[1] == WheelMetadataFileName
                : c.Path[1] == SdistMetadataFileName))
            .ToList();

        // A wheel that vendors another package may have its .dist-info folder too.
        var project = PyPIPackageName.Normalize(projectName);
        var (entry, _) = candidates.FirstOrDefault(c => PyPIPackageName.Normalize(c.Path[0].Split('-')[0]) == project);
        entry ??= candidates.FirstOrDefault().Entry;

        if (entry == null)
        {
            return null;
        }

        // The declared length may not be the real one, so the content is limited as it is read.
        using var entryStream = entry.Open();
        using var stream = new LimitedReadStream(
            entryStream,
            MaxMetadataBytes,
            () => new InvalidPyPIPackageException($"The metadata is larger than {MaxMetadataBytes / 1024 / 1024} MiB."));
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        return buffer.ToArray();
    }

    private static async Task<byte[]> ReadFromTarAsync(Stream content, CancellationToken cancellationToken)
    {
        await using var gzip = new GZipStream(content, CompressionMode.Decompress, leaveOpen: true);
        await using var limited = new LimitedReadStream(
            gzip,
            MaxUncompressedBytes,
            () => new InvalidPyPIPackageException(
                $"The sdist has no {SdistMetadataFileName} file in its first {MaxUncompressedBytes / 1024 / 1024} MiB."));

        // The tar format is the same for every ecosystem, and this reader is lenient about headers.
        var tar = new PubTarReader(limited);

        while (await tar.GetNextEntryAsync(cancellationToken) is { } entry)
        {
            var path = entry.Path.Replace('\\', '/');
            if (path.StartsWith("./", StringComparison.Ordinal))
            {
                path = path[2..];
            }

            var parts = path.Split('/');
            if (entry.IsFile && parts.Length == 2 && parts[1] == SdistMetadataFileName)
            {
                return await tar.ReadContentAsync(MaxMetadataBytes, cancellationToken);
            }
        }

        return null;
    }
}
