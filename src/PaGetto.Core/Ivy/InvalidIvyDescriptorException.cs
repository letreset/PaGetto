using System;

namespace PaGetto.Core.Ivy;

/// <summary>
/// Thrown for an <c>ivy.xml</c> that can't be read, or that is not the descriptor of the revision it
/// was uploaded to. The message is shown to the person publishing the module.
/// </summary>
public class InvalidIvyDescriptorException : Exception
{
    public InvalidIvyDescriptorException(string message)
        : base(message)
    {
    }

    public InvalidIvyDescriptorException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
