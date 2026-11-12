using System;

namespace PaGetto.Core.Maven;

/// <summary>
/// Thrown for a file that is larger than the feed allows.
/// </summary>
public class MavenFileTooLargeException : Exception
{
    public MavenFileTooLargeException(long maxBytes)
        : base($"The file is larger than the feed's limit of {maxBytes / (1024 * 1024)} MiB.")
    {
    }
}
