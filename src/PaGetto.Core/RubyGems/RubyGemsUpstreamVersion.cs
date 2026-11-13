using PaGetto.Core.Configuration;

namespace PaGetto.Core.RubyGems;

/// <summary>
/// A gem file a mirror lists: a line of its info file.
/// </summary>
public sealed class RubyGemsUpstreamVersion
{
    /// <summary>
    /// The gem name the mirror was asked for.
    /// </summary>
    public string Name { get; init; }

    public string Version { get; init; }

    /// <summary>
    /// The platform, <see cref="RubyGemsFileName.RubyPlatform"/> for a gem of pure Ruby.
    /// </summary>
    public string Platform { get; init; }

    /// <summary>
    /// The mirror's info line, which the feed serves as it is.
    /// </summary>
    public string Line { get; init; }

    /// <summary>
    /// The lowercase hex SHA-256 of the gem file, or null if the mirror lists none.
    /// </summary>
    public string Sha256 { get; init; }

    public MirrorOptions Mirror { get; init; }

    public string FullName => RubyGemsFileName.GetFullName(Name, Version, Platform);
}
