using System;

namespace PaGetto.Core.Npm;

/// <summary>
/// Thrown for a tarball or publish request that is not a valid npm package. The message is shown
/// to the person publishing the package.
/// </summary>
public class InvalidNpmPackageException : Exception
{
    public InvalidNpmPackageException(string message)
        : base(message)
    {
    }

    public InvalidNpmPackageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
