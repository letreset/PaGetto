using System;

namespace PaGetto.Core.Pub;

/// <summary>
/// Thrown for an archive that is not a valid pub package. The message is shown to the person
/// publishing the package.
/// </summary>
public class InvalidPubArchiveException : Exception
{
    public InvalidPubArchiveException(string message)
        : base(message)
    {
    }

    public InvalidPubArchiveException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
