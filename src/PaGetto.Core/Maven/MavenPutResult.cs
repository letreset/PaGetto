namespace PaGetto.Core.Maven;

/// <summary>
/// The result of <see cref="IMavenRepositoryService.PutAsync"/>.
/// </summary>
public sealed class MavenPutResult
{
    public static readonly MavenPutResult Success = new() { Status = MavenPutStatus.Success };

    public MavenPutStatus Status { get; init; }

    /// <summary>
    /// Why the upload was rejected, for the person publishing. Null on success.
    /// </summary>
    public string Message { get; init; }

    /// <summary>
    /// Whether a file of a version was stored, rather than a checksum or metadata the server
    /// computes itself.
    /// </summary>
    public bool StoredFile { get; init; }
}

public enum MavenPutStatus
{
    Success,

    /// <summary>
    /// The content is not valid for its path, e.g. a POM of another artifact or a wrong checksum.
    /// </summary>
    Invalid,

    /// <summary>
    /// The feed already has the file with other content, and does not allow overwriting it.
    /// </summary>
    Conflict,

    /// <summary>
    /// The file is larger than the feed allows.
    /// </summary>
    TooLarge,
}
