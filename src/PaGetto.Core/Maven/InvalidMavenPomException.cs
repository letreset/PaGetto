using System;

namespace PaGetto.Core.Maven;

/// <summary>
/// Thrown for a POM that can't be read, or that is not the POM of the version it was uploaded to.
/// The message is shown to the person publishing the package.
/// </summary>
public class InvalidMavenPomException : Exception
{
    public InvalidMavenPomException(string message)
        : base(message)
    {
    }

    public InvalidMavenPomException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
