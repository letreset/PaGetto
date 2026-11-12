using System;

namespace PaGetto.Core.PyPI;

/// <summary>
/// Thrown for a file that is not a valid distribution of the project it was uploaded for. The
/// message is shown to the person uploading it.
/// </summary>
public class InvalidPyPIPackageException : Exception
{
    public InvalidPyPIPackageException(string message)
        : base(message)
    {
    }

    public InvalidPyPIPackageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
