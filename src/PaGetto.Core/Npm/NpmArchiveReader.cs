using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Pub;

namespace PaGetto.Core.Npm;

/// <summary>
/// Reads an npm package tarball: a gzipped tar file whose files are in one folder, normally
/// <c>package/</c>, with a <c>package.json</c> in it.
/// </summary>
public static class NpmArchiveReader
{
    /// <summary>
    /// The largest tarball accepted once decompressed. A small upload can decompress to a huge
    /// archive, which would otherwise keep the server busy reading it.
    /// </summary>
    public const long MaxUncompressedBytes = 1024L * 1024 * 1024;

    private const int MaxTextFileBytes = 1024 * 1024;

    private const string ManifestFileName = "package.json";

    /// <summary>
    /// Reads the whole tarball, so a truncated or corrupt one is rejected.
    /// </summary>
    /// <param name="tarball">The tarball. It is read from its current position and left open.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <exception cref="InvalidNpmPackageException">The tarball is not a valid npm package.</exception>
    public static async Task<NpmArchive> ReadAsync(Stream tarball, CancellationToken cancellationToken)
    {
        string manifestJson = null;
        string readme = null;

        try
        {
            await using var gzip = new GZipStream(tarball, CompressionMode.Decompress, leaveOpen: true);
            await using var limited = new LimitedReadStream(
                gzip,
                MaxUncompressedBytes,
                () => new InvalidNpmPackageException(
                    $"The package is larger than {MaxUncompressedBytes / 1024 / 1024} MiB when decompressed."));

            // The tar format is the same for every ecosystem, and this reader is lenient about headers.
            var tar = new PubTarReader(limited);

            while (await tar.GetNextEntryAsync(cancellationToken) is { } entry)
            {
                if (!entry.IsFile)
                {
                    continue;
                }

                // The npm client reads the files of the package's folder, whatever it is called.
                var path = entry.Path.Replace('\\', '/');
                if (path.StartsWith("./", StringComparison.Ordinal))
                {
                    path = path[2..];
                }

                var slash = path.IndexOf('/', StringComparison.Ordinal);
                if (slash <= 0 || path.IndexOf('/', slash + 1) >= 0)
                {
                    continue;
                }

                var fileName = path[(slash + 1)..];
                if (manifestJson == null && fileName == ManifestFileName)
                {
                    manifestJson = ReadText(await tar.ReadContentAsync(MaxTextFileBytes, cancellationToken));
                }
                else if (readme == null && IsReadme(fileName))
                {
                    readme = ReadText(await tar.ReadContentAsync(MaxTextFileBytes, cancellationToken));
                }
            }
        }
        catch (InvalidPubArchiveException e)
        {
            // Thrown by the tar reader.
            throw new InvalidNpmPackageException("The package is not a valid tar archive.", e);
        }
        catch (InvalidDataException e)
        {
            // The data is not gzip, or the gzip stream is corrupt.
            throw new InvalidNpmPackageException("The package is not a valid gzipped tar archive.", e);
        }

        if (manifestJson == null)
        {
            throw new InvalidNpmPackageException("The package has no package.json.");
        }

        JsonObject manifest;
        try
        {
            manifest = JsonNode.Parse(manifestJson) as JsonObject;
        }
        catch (JsonException e)
        {
            throw new InvalidNpmPackageException("The package's package.json is not valid JSON.", e);
        }

        if (manifest == null)
        {
            throw new InvalidNpmPackageException("The package's package.json is not a JSON object.");
        }

        var name = GetString(manifest, "name");
        if (!NpmPackageName.IsValid(name))
        {
            throw new InvalidNpmPackageException(
                $"The name in package.json is not a valid npm package name of at most {NpmPackageName.MaxLength} characters.");
        }

        if (!NpmVersion.TryParse(GetString(manifest, "version"), out var version))
        {
            throw new InvalidNpmPackageException("The version in package.json must be a semantic version, e.g. 1.2.3.");
        }

        return new NpmArchive
        {
            Name = name,
            Version = version,
            Manifest = manifest,
            Readme = readme,
        };
    }

    private static string GetString(JsonObject json, string property)
    {
        return json[property] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }

    private static bool IsReadme(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);

        return name.Equals("readme", StringComparison.OrdinalIgnoreCase)
            && (extension.Length == 0
                || extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase));
    }

    private static string ReadText(byte[] content)
    {
        // The reader skips a byte order mark.
        using var reader = new StreamReader(new MemoryStream(content), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
