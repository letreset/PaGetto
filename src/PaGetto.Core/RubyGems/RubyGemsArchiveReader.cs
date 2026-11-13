using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Pub;

namespace PaGetto.Core.RubyGems;

/// <summary>
/// Reads a <c>.gem</c> file: a tar archive (not compressed) with the gemspec as gzipped YAML in
/// <c>metadata.gz</c>, the gem's files as a gzipped tar archive in <c>data.tar.gz</c>, and their
/// checksums in <c>checksums.yaml.gz</c>.
/// See: https://guides.rubygems.org/specification-reference/
/// </summary>
public static class RubyGemsArchiveReader
{
    /// <summary>
    /// The largest <c>data.tar.gz</c> searched for a readme. A larger gem is stored without one.
    /// </summary>
    public const int MaxDataBytes = 64 * 1024 * 1024;

    private const int MaxMetadataBytes = 16 * 1024 * 1024;
    private const int MaxReadmeBytes = 1024 * 1024;

    private const string MetadataFileName = "metadata.gz";
    private const string DataFileName = "data.tar.gz";

    // The readmes shown in the web UI, which renders Markdown, in order of preference.
    private static readonly string[] _readmeFileNames = ["README.md", "README.markdown", "README"];

    /// <summary>
    /// Reads the whole gem, so a truncated one is rejected.
    /// </summary>
    /// <param name="gem">The gem. It is read from its current position and left open.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <exception cref="InvalidRubyGemException">The file is not a gem.</exception>
    public static async Task<RubyGemsArchive> ReadAsync(Stream gem, CancellationToken cancellationToken)
    {
        RubyGemsSpecification specification = null;
        string readme = null;

        try
        {
            // The tar format is the same for every ecosystem, and this reader is lenient about headers.
            var tar = new PubTarReader(gem);

            while (await tar.GetNextEntryAsync(cancellationToken) is { } entry)
            {
                if (!entry.IsFile)
                {
                    continue;
                }

                if (entry.Path == MetadataFileName && specification == null)
                {
                    var metadata = await tar.ReadContentAsync(MaxMetadataBytes, cancellationToken);
                    specification = RubyGemsSpecificationYaml.Parse(await GunzipTextAsync(metadata, MaxMetadataBytes, cancellationToken));
                }
                else if (entry.Path == DataFileName && entry.Size <= MaxDataBytes)
                {
                    readme = await ReadReadmeAsync(await tar.ReadContentAsync(MaxDataBytes, cancellationToken), cancellationToken);
                }
            }
        }
        catch (InvalidPubArchiveException e)
        {
            // Thrown by the tar reader.
            throw new InvalidRubyGemException("The gem is not a valid tar archive.", e);
        }

        if (specification == null)
        {
            throw new InvalidRubyGemException($"The gem has no {MetadataFileName}.");
        }

        return new RubyGemsArchive { Specification = specification, Readme = readme };
    }

    /// <summary>
    /// Finds the readme at the root of the gem's files. A <c>data.tar.gz</c> that can't be read
    /// only means there is no readme: RubyGems checks the files when it installs the gem.
    /// </summary>
    private static async Task<string> ReadReadmeAsync(byte[] data, CancellationToken cancellationToken)
    {
        string found = null;
        var foundRank = int.MaxValue;

        try
        {
            await using var gzip = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
            await using var limited = new LimitedReadStream(gzip, MaxDataBytes * 4L, () => new InvalidDataException("data.tar.gz is too large."));
            var tar = new PubTarReader(limited);

            while (await tar.GetNextEntryAsync(cancellationToken) is { } entry)
            {
                var path = entry.Path.StartsWith("./", StringComparison.Ordinal) ? entry.Path[2..] : entry.Path;
                var rank = Array.FindIndex(_readmeFileNames, n => n.Equals(path, StringComparison.OrdinalIgnoreCase));
                if (entry.IsFile && rank >= 0 && rank < foundRank && entry.Size <= MaxReadmeBytes)
                {
                    found = ReadText(await tar.ReadContentAsync(MaxReadmeBytes, cancellationToken));
                    foundRank = rank;
                }
            }
        }
        catch (Exception e) when (e is InvalidPubArchiveException or InvalidDataException)
        {
            return null;
        }

        return found;
    }

    private static async Task<string> GunzipTextAsync(byte[] content, int maxBytes, CancellationToken cancellationToken)
    {
        try
        {
            await using var gzip = new GZipStream(new MemoryStream(content), CompressionMode.Decompress);
            await using var limited = new LimitedReadStream(
                gzip,
                maxBytes,
                () => new InvalidRubyGemException($"{MetadataFileName} is larger than {maxBytes / 1024 / 1024} MiB when decompressed."));

            using var text = new MemoryStream();
            await limited.CopyToAsync(text, cancellationToken);

            return ReadText(text.ToArray());
        }
        catch (InvalidDataException e)
        {
            throw new InvalidRubyGemException($"{MetadataFileName} is not valid gzip data.", e);
        }
    }

    private static string ReadText(byte[] content)
    {
        // The reader skips a byte order mark.
        using var reader = new StreamReader(new MemoryStream(content), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
