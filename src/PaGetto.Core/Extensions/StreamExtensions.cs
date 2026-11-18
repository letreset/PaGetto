using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Artifacts;

namespace PaGetto.Core.Extensions;

public static class StreamExtensions
{
    private const int DefaultCopyBufferSize = 81920;

    /// <summary>
    /// Copies a stream to a file, and returns that file as a stream. The underlying file will be
    /// deleted when the resulting stream is disposed.
    /// </summary>
    /// <param name="original">The stream to be copied, at its current position.</param>
    /// <param name="cancellationToken"></param>
    /// <returns>The copied stream, with its position reset to the beginning.</returns>
    public static async Task<FileStream> AsTemporaryFileStreamAsync(
        this Stream original,
        CancellationToken cancellationToken = default)
    {
        var result = new FileStream(
            Path.GetTempFileName(),
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.None,
            DefaultCopyBufferSize,
            FileOptions.DeleteOnClose);

        try
        {
            await original.CopyToAsync(result, DefaultCopyBufferSize, cancellationToken);
            result.Position = 0;
        }
        catch (Exception)
        {
            result.Dispose();
            throw;
        }

        return result;
    }

    /// <summary>
    /// Copies at most <paramref name="maxBytes"/> of a stream to a file, and returns that file as a
    /// stream. The underlying file will be deleted when the resulting stream is disposed.
    /// </summary>
    /// <exception cref="ArtifactFileTooLargeException">The stream has more than <paramref name="maxBytes"/>.</exception>
    public static async Task<FileStream> AsTemporaryFileStreamAsync(
        this Stream original,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        await using var limited = new LimitedReadStream(original, maxBytes, () => new ArtifactFileTooLargeException(maxBytes));
        return await limited.AsTemporaryFileStreamAsync(cancellationToken);
    }

    public static bool Matches(this Stream content, Stream target)
    {
        using var sha256 = SHA256.Create();
        var contentHash = sha256.ComputeHash(content);
        var targetHash = sha256.ComputeHash(target);

        return contentHash.SequenceEqual(targetHash);
    }
}
