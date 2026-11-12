using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Entities;

namespace PaGetto.Core.Maven;

/// <summary>
/// A file read into a temporary file, with the checksums a Maven repository serves for it.
/// </summary>
public sealed class MavenFileContent : IDisposable
{
    private const int BufferSize = 81920;

    private MavenFileContent()
    {
    }

    /// <summary>
    /// The content, positioned at the start. Deleted when this is disposed.
    /// </summary>
    public FileStream Stream { get; private init; }

    public long Size { get; private init; }
    public string Sha1 { get; private init; }
    public string Md5 { get; private init; }
    public string Sha256 { get; private init; }
    public string Sha512 { get; private init; }

    /// <summary>
    /// Reads a stream to its end into a temporary file, computing its checksums on the way.
    /// </summary>
    /// <param name="source">The content.</param>
    /// <param name="maxBytes">The most the content may have.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <exception cref="MavenFileTooLargeException">The content has more than <paramref name="maxBytes"/>.</exception>
    public static async Task<MavenFileContent> ReadAsync(Stream source, long maxBytes, CancellationToken cancellationToken)
    {
        var file = new FileStream(
            Path.GetTempFileName(),
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.None,
            BufferSize,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous);

        try
        {
#pragma warning disable CA5350, CA5351 // SHA-1 and MD5 are what Maven clients check files with, not for security.
            using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
#pragma warning restore CA5350, CA5351
            using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var sha512 = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);

            var limited = new LimitedReadStream(source, maxBytes, () => new MavenFileTooLargeException(maxBytes));
            var buffer = new byte[BufferSize];
            long size = 0;
            int read;

            while ((read = await limited.ReadAsync(buffer, cancellationToken)) > 0)
            {
                var chunk = buffer.AsSpan(0, read);
                sha1.AppendData(chunk);
                md5.AppendData(chunk);
                sha256.AppendData(chunk);
                sha512.AppendData(chunk);

                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                size += read;
            }

            await file.FlushAsync(cancellationToken);
            file.Position = 0;

            return new MavenFileContent
            {
                Stream = file,
                Size = size,
                Sha1 = Convert.ToHexStringLower(sha1.GetHashAndReset()),
                Md5 = Convert.ToHexStringLower(md5.GetHashAndReset()),
                Sha256 = Convert.ToHexStringLower(sha256.GetHashAndReset()),
                Sha512 = Convert.ToHexStringLower(sha512.GetHashAndReset()),
            };
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// The row that describes this content as a file of a version.
    /// </summary>
    public ArtifactFile ToArtifactFile(string fileName, DateTime createdAtUtc)
    {
        return new ArtifactFile
        {
            FileName = fileName,
            Size = Size,
            Sha1 = Sha1,
            Md5 = Md5,
            Sha256 = Sha256,
            Sha512 = Sha512,
            CreatedAtUtc = createdAtUtc,
        };
    }

    public void Dispose()
    {
        Stream?.Dispose();
    }
}
