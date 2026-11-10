using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PaGetto.Core.Pub;

/// <summary>
/// Reads the entries of a tar stream, front to back. It only reads what a pub package needs (paths,
/// sizes and file content), and accepts the headers the pub client writes: numbers padded with
/// spaces and a ustar version other than "00", which <c>System.Formats.Tar</c> rejects. Long paths
/// are read from GNU long name entries, PAX extended headers and the ustar prefix.
/// </summary>
internal sealed class PubTarReader
{
    private const int BlockSize = 512;

    private readonly Stream _stream;
    private readonly byte[] _header = new byte[BlockSize];

    // Bytes of the current entry that have not been read yet: its content, then the padding to a full block.
    private long _unreadContent;
    private long _unreadPadding;

    public PubTarReader(Stream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    /// <summary>
    /// Moves to the next file or directory entry, skipping what is left of the current one.
    /// </summary>
    /// <returns>The entry, or null at the end of the archive.</returns>
    /// <exception cref="InvalidPubArchiveException">The stream is not a tar archive, or it is cut off.</exception>
    public async Task<PubTarEntry> GetNextEntryAsync(CancellationToken cancellationToken)
    {
        string pathOverride = null;
        long? sizeOverride = null;

        while (true)
        {
            await SkipAsync(_unreadContent + _unreadPadding, cancellationToken);
            _unreadContent = _unreadPadding = 0;

            var read = await _stream.ReadAtLeastAsync(_header, BlockSize, throwOnEndOfStream: false, cancellationToken);
            if (read == 0 || (read == BlockSize && _header.AsSpan().IndexOfAnyExcept((byte)0) < 0))
            {
                // The end marker is two empty blocks. Archives that just end are accepted too.
                return null;
            }

            if (read < BlockSize || !HasValidChecksum(_header))
            {
                throw NotATarArchive();
            }

            var size = sizeOverride ?? ParseNumber(_header.AsSpan(124, 12));
            _unreadContent = size;
            _unreadPadding = (BlockSize - size % BlockSize) % BlockSize;

            var type = (char)_header[156];
            switch (type)
            {
                case 'L':
                    // GNU: this entry's content is the path of the next entry.
                    pathOverride = ReadText(await ReadContentAsync(maxBytes: short.MaxValue, cancellationToken));
                    continue;

                case 'x':
                    // PAX: this entry's content has attributes of the next entry.
                    var attributes = await ReadContentAsync(maxBytes: 1024 * 1024, cancellationToken);
                    ReadPaxAttributes(attributes, ref pathOverride, ref sizeOverride);
                    continue;

                case 'g':
                    // PAX attributes for the whole archive. None of them matter here.
                    continue;
            }

            return new PubTarEntry
            {
                Path = pathOverride ?? ReadHeaderPath(),
                Size = size,
                IsFile = type is '0' or '\0' or '7',
            };
        }
    }

    /// <summary>
    /// Reads the content of the current entry.
    /// </summary>
    /// <param name="maxBytes">The largest content accepted.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <exception cref="InvalidPubArchiveException">The content is larger than <paramref name="maxBytes"/>, or the archive is cut off.</exception>
    public async Task<byte[]> ReadContentAsync(int maxBytes, CancellationToken cancellationToken)
    {
        if (_unreadContent > maxBytes)
        {
            throw new InvalidPubArchiveException($"A file in the package is larger than the {maxBytes / 1024} KiB that are read from it.");
        }

        var content = new byte[_unreadContent];
        var read = await _stream.ReadAtLeastAsync(content, content.Length, throwOnEndOfStream: false, cancellationToken);
        if (read < content.Length)
        {
            throw NotATarArchive();
        }

        _unreadContent = 0;
        return content;
    }

    private async Task SkipAsync(long count, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        while (count > 0)
        {
            var read = await _stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, count)), cancellationToken);
            if (read == 0)
            {
                throw NotATarArchive();
            }

            count -= read;
        }
    }

    private string ReadHeaderPath()
    {
        var name = ReadText(_header.AsSpan(0, 100));

        // POSIX ustar splits long paths over the prefix and name fields. GNU headers ("ustar  ")
        // use that space for other things.
        var isPosix = _header.AsSpan(257, 6).SequenceEqual("ustar\0"u8);
        var prefix = isPosix ? ReadText(_header.AsSpan(345, 155)) : string.Empty;

        return prefix.Length > 0 ? prefix + "/" + name : name;
    }

    /// <summary>
    /// The checksum is the sum of the header's bytes, with the checksum field itself counted as spaces.
    /// </summary>
    private static bool HasValidChecksum(ReadOnlySpan<byte> header)
    {
        long sum = 0;
        for (var i = 0; i < header.Length; i++)
        {
            sum += i is >= 148 and < 156 ? (byte)' ' : header[i];
        }

        try
        {
            return ParseNumber(header.Slice(148, 8)) == sum;
        }
        catch (InvalidPubArchiveException)
        {
            return false;
        }
    }

    /// <summary>
    /// Numbers are octal text, padded with spaces or NULs on either side. Large numbers can be
    /// binary instead, marked by the first byte's high bit.
    /// </summary>
    private static long ParseNumber(ReadOnlySpan<byte> field)
    {
        if ((field[0] & 0x80) != 0)
        {
            long binary = field[0] & 0x7F;
            foreach (var value in field[1..])
            {
                if (binary > long.MaxValue >> 8) throw NotATarArchive();
                binary = (binary << 8) | value;
            }

            return binary;
        }

        long number = 0;
        foreach (var value in field.Trim(" \0"u8))
        {
            if (value is < (byte)'0' or > (byte)'7') throw NotATarArchive();
            number = (number << 3) | (uint)(value - '0');
        }

        return number;
    }

    /// <summary>
    /// A PAX header is a list of records "length key=value\n", where the length counts the whole record.
    /// </summary>
    private static void ReadPaxAttributes(ReadOnlySpan<byte> content, ref string path, ref long? size)
    {
        while (!content.IsEmpty)
        {
            var space = content.IndexOf((byte)' ');
            if (space <= 0
                || !int.TryParse(Encoding.ASCII.GetString(content[..space]), NumberStyles.None, CultureInfo.InvariantCulture, out var length)
                || length <= space + 1
                || length > content.Length)
            {
                throw NotATarArchive();
            }

            // Without the length, its space and the newline at the end.
            var record = content.Slice(space + 1, length - space - 2);
            var equals = record.IndexOf((byte)'=');
            if (equals > 0)
            {
                var key = Encoding.ASCII.GetString(record[..equals]);
                var value = Encoding.UTF8.GetString(record[(equals + 1)..]);

                if (key == "path")
                {
                    path = value;
                }
                else if (key == "size" && long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var paxSize))
                {
                    size = paxSize;
                }
            }

            content = content[length..];
        }
    }

    /// <summary>
    /// Text fields end at the first NUL, or fill the whole field.
    /// </summary>
    private static string ReadText(ReadOnlySpan<byte> field)
    {
        var end = field.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end < 0 ? field : field[..end]);
    }

    private static InvalidPubArchiveException NotATarArchive()
    {
        return new InvalidPubArchiveException("The package is not a valid gzipped tar archive.");
    }
}
