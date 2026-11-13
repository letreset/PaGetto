namespace PaGetto.Core.RubyGems;

/// <summary>
/// What a gem file says about itself (see <see cref="RubyGemsArchiveReader"/>).
/// </summary>
public sealed class RubyGemsArchive
{
    public RubyGemsSpecification Specification { get; init; }

    /// <summary>
    /// The gem's readme, or null if it has none.
    /// </summary>
    public string Readme { get; init; }
}
