using System;

namespace PaGetto.Core.Vsix;

/// <summary>
/// Thrown for an uploaded extension that is not valid. The message is shown to the person
/// uploading it.
/// </summary>
public class InvalidVsixPackageException : Exception
{
    public InvalidVsixPackageException(string message)
        : base(message)
    {
    }

    public InvalidVsixPackageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
