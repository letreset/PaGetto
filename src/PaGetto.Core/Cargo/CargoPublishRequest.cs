using System;
using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Artifacts;

namespace PaGetto.Core.Cargo;

/// <summary>
/// The body of <c>cargo publish</c>: the length of the JSON metadata as a 32-bit little-endian
/// number, the metadata, the length of the <c>.crate</c> file the same way, and the file.
/// See: https://doc.rust-lang.org/cargo/reference/registry-web-api.html#publish
/// </summary>
public sealed class CargoPublishRequest : IDisposable
{
    /// <summary>
    /// The largest metadata accepted. It carries the readme, so it can be long.
    /// </summary>
    public const long MaxMetadataBytes = 16L * 1024 * 1024;

    private const int CopyBufferSize = 81920;

    private CargoPublishRequest(JsonObject metadata, Stream crate)
    {
        Metadata = metadata;
        Crate = crate;
    }

    /// <summary>
    /// The metadata of the crate: its name, version, dependencies, features and the fields of its
    /// <c>Cargo.toml</c>.
    /// </summary>
    public JsonObject Metadata { get; }

    /// <summary>
    /// The <c>.crate</c> file, in a seekable temporary file.
    /// </summary>
    public Stream Crate { get; }

    /// <summary>
    /// Reads a publish body.
    /// </summary>
    /// <param name="body">The body. It is read to the end of the crate file.</param>
    /// <param name="maxCrateBytes">The largest crate file the feed accepts.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <exception cref="InvalidCargoPackageException">The body is not a publish request.</exception>
    /// <exception cref="ArtifactFileTooLargeException">The crate file is larger than <paramref name="maxCrateBytes"/>.</exception>
    public static async Task<CargoPublishRequest> ReadAsync(Stream body, long maxCrateBytes, CancellationToken cancellationToken)
    {
        var metadataLength = await ReadLengthAsync(body, cancellationToken);
        if (metadataLength > MaxMetadataBytes)
        {
            throw new InvalidCargoPackageException($"The metadata of the crate is larger than {MaxMetadataBytes / 1024 / 1024} MiB.");
        }

        var metadataBytes = new byte[metadataLength];
        await ReadExactlyAsync(body, metadataBytes, cancellationToken);

        JsonObject metadata;
        try
        {
            metadata = JsonNode.Parse(metadataBytes) as JsonObject;
        }
        catch (JsonException e)
        {
            throw new InvalidCargoPackageException("The metadata of the crate is not valid JSON.", e);
        }

        if (metadata == null)
        {
            throw new InvalidCargoPackageException("The metadata of the crate is not a JSON object.");
        }

        var crateLength = await ReadLengthAsync(body, cancellationToken);
        if (crateLength > maxCrateBytes)
        {
            throw new ArtifactFileTooLargeException(maxCrateBytes);
        }

        return new CargoPublishRequest(metadata, await CopyToTemporaryFileAsync(body, crateLength, cancellationToken));
    }

    public void Dispose()
    {
        Crate.Dispose();
    }

    /// <summary>
    /// Copies the next <paramref name="length"/> bytes of the body to a temporary file, which is
    /// deleted when the returned stream is disposed.
    /// </summary>
    private static async Task<Stream> CopyToTemporaryFileAsync(Stream body, long length, CancellationToken cancellationToken)
    {
        var file = new FileStream(
            Path.GetTempFileName(),
            FileMode.Create,
            FileAccess.ReadWrite,
            FileShare.None,
            CopyBufferSize,
            FileOptions.DeleteOnClose);

        try
        {
            var buffer = new byte[CopyBufferSize];
            var remaining = length;
            while (remaining > 0)
            {
                var read = await body.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken);
                if (read == 0)
                {
                    throw TruncatedBody();
                }

                await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                remaining -= read;
            }

            file.Position = 0;
            return file;
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
    }

    private static async Task<long> ReadLengthAsync(Stream body, CancellationToken cancellationToken)
    {
        var bytes = new byte[4];
        await ReadExactlyAsync(body, bytes, cancellationToken);

        return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }

    private static async Task ReadExactlyAsync(Stream body, byte[] buffer, CancellationToken cancellationToken)
    {
        var read = await body.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken);
        if (read < buffer.Length)
        {
            throw TruncatedBody();
        }
    }

    private static InvalidCargoPackageException TruncatedBody()
    {
        return new InvalidCargoPackageException("The publish request ends before the end of the crate.");
    }
}
