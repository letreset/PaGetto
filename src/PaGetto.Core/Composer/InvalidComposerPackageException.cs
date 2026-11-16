using System;

namespace PaGetto.Core.Composer;

/// <summary>
/// Thrown for an uploaded package zip that is not valid. The message is shown to the person
/// uploading it.
/// </summary>
public class InvalidComposerPackageException : Exception
{
    public InvalidComposerPackageException(string message)
        : base(message)
    {
    }

    public InvalidComposerPackageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
