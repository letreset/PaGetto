using System;

namespace PaGetto.Core.Swift;

/// <summary>
/// Thrown for a published source archive or metadata that is not valid. The message is shown to the
/// person publishing it.
/// </summary>
public class InvalidSwiftPackageException : Exception
{
    public InvalidSwiftPackageException(string message)
        : base(message)
    {
    }

    public InvalidSwiftPackageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
