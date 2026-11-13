using System;

namespace PaGetto.Core.Go;

/// <summary>
/// Thrown for an uploaded module zip that is not valid. The message is shown to the person
/// uploading it.
/// </summary>
public class InvalidGoModuleException : Exception
{
    public InvalidGoModuleException(string message)
        : base(message)
    {
    }

    public InvalidGoModuleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
