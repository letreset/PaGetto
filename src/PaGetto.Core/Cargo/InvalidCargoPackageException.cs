using System;

namespace PaGetto.Core.Cargo;

/// <summary>
/// Thrown for a publish request or crate file that is not a valid crate. The message is shown to
/// the person publishing the crate.
/// </summary>
public class InvalidCargoPackageException : Exception
{
    public InvalidCargoPackageException(string message)
        : base(message)
    {
    }

    public InvalidCargoPackageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
