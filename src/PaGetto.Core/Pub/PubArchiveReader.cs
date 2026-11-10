using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace PaGetto.Core.Pub;

/// <summary>
/// Reads a pub package archive: a gzipped tar file with a <c>pubspec.yaml</c> at its root.
/// </summary>
public static class PubArchiveReader
{
    /// <summary>
    /// The largest archive accepted once decompressed. A small upload can decompress to a huge
    /// archive, which would otherwise keep the server busy reading it.
    /// </summary>
    public const long MaxUncompressedBytes = 1024L * 1024 * 1024;

    private const int MaxTextFileBytes = 1024 * 1024;

    private const string PubspecFileName = "pubspec.yaml";
    private const string ReadmeFileName = "readme.md";

    /// <summary>
    /// Reads the whole archive, so a truncated or corrupt one is rejected.
    /// </summary>
    /// <param name="archive">The archive. It is read from its current position and left open.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <exception cref="InvalidPubArchiveException">The archive is not a valid pub package.</exception>
    public static async Task<PubArchive> ReadAsync(Stream archive, CancellationToken cancellationToken)
    {
        string pubspecYaml = null;
        string readme = null;

        try
        {
            await using var gzip = new GZipStream(archive, CompressionMode.Decompress, leaveOpen: true);
            await using var limited = new LimitedReadStream(gzip, MaxUncompressedBytes);
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

                if (path == PubspecFileName)
                {
                    pubspecYaml = ReadText(await tar.ReadContentAsync(MaxTextFileBytes, cancellationToken));
                }
                else if (readme == null && path.Equals(ReadmeFileName, StringComparison.OrdinalIgnoreCase))
                {
                    readme = ReadText(await tar.ReadContentAsync(MaxTextFileBytes, cancellationToken));
                }
            }
        }
        catch (InvalidDataException e)
        {
            // The data is not gzip, or the gzip stream is corrupt.
            throw new InvalidPubArchiveException("The package is not a valid gzipped tar archive.", e);
        }

        if (pubspecYaml == null)
        {
            throw new InvalidPubArchiveException("The package has no pubspec.yaml at its root.");
        }

        var pubspec = PubspecYaml.ToJson(pubspecYaml);

        var name = GetString(pubspec, "name");
        if (!PubPackageName.IsValid(name))
        {
            throw new InvalidPubArchiveException(
                "The package name in pubspec.yaml must be a lowercase Dart identifier (a-z, 0-9 and _) " +
                $"of at most {PubPackageName.MaxLength} characters.");
        }

        if (!PubVersion.TryParse(GetString(pubspec, "version"), out var version))
        {
            throw new InvalidPubArchiveException("The version in pubspec.yaml must be a semantic version, e.g. 1.2.3.");
        }

        return new PubArchive
        {
            Name = name,
            Version = version,
            Pubspec = pubspec,
            Readme = readme,
        };
    }

    private static string GetString(JsonObject pubspec, string property)
    {
        return pubspec[property] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }

    private static string ReadText(byte[] content)
    {
        // The reader skips a byte order mark.
        using var reader = new StreamReader(new MemoryStream(content), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Fails once more than a number of bytes were read from the inner stream.
    /// </summary>
    private sealed class LimitedReadStream : Stream
    {
        private readonly Stream _inner;
        private long _remaining;

        public LimitedReadStream(Stream inner, long limit)
        {
            _inner = inner;
            _remaining = limit;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Count(_inner.Read(buffer, offset, count));
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return Count(await _inner.ReadAsync(buffer, cancellationToken));
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        private int Count(int read)
        {
            _remaining -= read;
            if (_remaining < 0)
            {
                throw new InvalidPubArchiveException(
                    $"The package is larger than {MaxUncompressedBytes / 1024 / 1024} MiB when decompressed.");
            }

            return read;
        }
    }
}
