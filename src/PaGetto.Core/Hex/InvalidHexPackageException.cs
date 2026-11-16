using System;

namespace PaGetto.Core.Hex;

/// <summary>
/// Thrown for a tarball that is not a valid Hex package. The message is shown to the person
/// publishing the package.
/// </summary>
public class InvalidHexPackageException : Exception
{
    public InvalidHexPackageException(string message)
        : base(message)
    {
    }

    public InvalidHexPackageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
