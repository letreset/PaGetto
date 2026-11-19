namespace PaGetto.Core.VsCode;

/// <summary>
/// The result of <see cref="IVsCodePackageService.UploadAsync"/>.
/// </summary>
public sealed class VsCodeUploadResult
{
    public VsCodeUploadStatus Status { get; init; }

    /// <summary>
    /// The extension's identifier, <c>publisher.name</c>, as its manifest has it. Null when the file could not be read.
    /// </summary>
    public string Id { get; init; }

    /// <summary>
    /// The version, as its manifest has it. Null when the file could not be read.
    /// </summary>
    public string Version { get; init; }

    /// <summary>
    /// The platform of the file. Null when the file could not be read.
    /// </summary>
    public string TargetPlatform { get; init; }

    /// <summary>
    /// Why the file was rejected, for the person uploading it. Null on success.
    /// </summary>
    public string Message { get; init; }
}
