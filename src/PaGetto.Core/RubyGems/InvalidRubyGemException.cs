using System;

namespace PaGetto.Core.RubyGems;

/// <summary>
/// Thrown for a file that is not a valid gem. The message is shown to the person pushing the gem.
/// </summary>
public class InvalidRubyGemException : Exception
{
    public InvalidRubyGemException(string message)
        : base(message)
    {
    }

    public InvalidRubyGemException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
