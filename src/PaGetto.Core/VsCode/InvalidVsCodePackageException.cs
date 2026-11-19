using System;

namespace PaGetto.Core.VsCode;

/// <summary>
/// Thrown for a VS Code extension that is not valid. The message is shown to the person uploading it.
/// </summary>
public class InvalidVsCodePackageException : Exception
{
    public InvalidVsCodePackageException(string message)
        : base(message)
    {
    }

    public InvalidVsCodePackageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
