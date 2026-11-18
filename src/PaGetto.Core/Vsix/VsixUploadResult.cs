namespace PaGetto.Core.Vsix;

/// <summary>
/// The result of <see cref="IVsixPackageService.UploadAsync"/>.
/// </summary>
public sealed class VsixUploadResult
{
    public VsixUploadStatus Status { get; init; }

    /// <summary>
    /// The extension's identifier, as its manifest has it. Null when the file could not be read.
    /// </summary>
    public string Id { get; init; }

    /// <summary>
    /// The version, as its manifest has it. Null when the file could not be read.
    /// </summary>
    public string Version { get; init; }

    /// <summary>
    /// Why the file was rejected, for the person uploading it. Null on success.
    /// </summary>
    public string Message { get; init; }
}
