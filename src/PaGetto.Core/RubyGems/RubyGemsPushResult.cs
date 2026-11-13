namespace PaGetto.Core.RubyGems;

/// <summary>
/// The outcome of <see cref="IRubyGemsPackageService.PushAsync"/>.
/// </summary>
public sealed class RubyGemsPushResult
{
    public RubyGemsPushStatus Status { get; init; }

    /// <summary>
    /// The gem name as published, or null if the gem had no valid one.
    /// </summary>
    public string Name { get; init; }

    /// <summary>
    /// The version, or null if the gem had no valid one.
    /// </summary>
    public string Version { get; init; }

    /// <summary>
    /// The full name of the gem file, e.g. <c>tool-1.0.0-java</c>, or null if the gem was not valid.
    /// </summary>
    public string FullName { get; init; }

    /// <summary>
    /// Why the push was rejected, shown by <c>gem push</c>. Null on success.
    /// </summary>
    public string Message { get; init; }
}

public enum RubyGemsPushStatus
{
    /// <summary>
    /// The gem is stored, or the same gem was stored already.
    /// </summary>
    Success,

    /// <summary>
    /// The version has a gem of this platform with other content, and the feed doesn't allow overwriting it.
    /// </summary>
    Conflict,

    /// <summary>
    /// The gem is larger than the feed allows.
    /// </summary>
    TooLarge,

    /// <summary>
    /// The file is not a valid gem.
    /// </summary>
    Invalid,
}
