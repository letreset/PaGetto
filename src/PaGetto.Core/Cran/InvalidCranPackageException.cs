using System;

namespace PaGetto.Core.Cran;

/// <summary>
/// Thrown for an uploaded source package that is not valid. The message is shown to the person
/// uploading it.
/// </summary>
public class InvalidCranPackageException : Exception
{
    public InvalidCranPackageException(string message)
        : base(message)
    {
    }

    public InvalidCranPackageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
