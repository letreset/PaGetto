using System;

namespace PaGetto.Core.Artifacts;

/// <summary>
/// Thrown for a file that is larger than the feed allows.
/// </summary>
public class ArtifactFileTooLargeException : Exception
{
    public ArtifactFileTooLargeException(long maxBytes)
        : base($"The file is larger than the feed's limit of {maxBytes / (1024 * 1024)} MiB.")
    {
    }
}
