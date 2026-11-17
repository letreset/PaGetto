using System;

namespace PaGetto.Core.CocoaPods;

/// <summary>
/// Thrown for a published podspec or source archive that is not valid. The message is shown to the
/// person publishing it.
/// </summary>
public class InvalidPodspecException : Exception
{
    public InvalidPodspecException(string message)
        : base(message)
    {
    }

    public InvalidPodspecException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
