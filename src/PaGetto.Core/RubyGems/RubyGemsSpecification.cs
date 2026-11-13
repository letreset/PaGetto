using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PaGetto.Core.RubyGems;

/// <summary>
/// The parts of a gemspec this server keeps: what the compact index, <c>gem install</c> and the web
/// UI need. It is read from the <c>metadata.gz</c> of a gem, and stored as JSON in the manifest of
/// the gem's version, one per platform (see <see cref="RubyGemsManifest"/>).
/// </summary>
public sealed class RubyGemsSpecification
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; }

    /// <summary>
    /// The platform, <see cref="RubyGemsFileName.RubyPlatform"/> for a gem of pure Ruby.
    /// </summary>
    [JsonPropertyName("platform")]
    public string Platform { get; set; } = RubyGemsFileName.RubyPlatform;

    [JsonPropertyName("summary")]
    public string Summary { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; }

    [JsonPropertyName("authors")]
    public List<string> Authors { get; set; } = [];

    [JsonPropertyName("email")]
    public List<string> Email { get; set; } = [];

    [JsonPropertyName("homepage")]
    public string Homepage { get; set; }

    [JsonPropertyName("licenses")]
    public List<string> Licenses { get; set; } = [];

    /// <summary>
    /// The gemspec's metadata, such as <c>source_code_uri</c> and <c>changelog_uri</c>.
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; set; } = [];

    /// <summary>
    /// The Ruby versions the gem works with, e.g. <c>&gt;= 3.0</c>. Empty for any.
    /// </summary>
    [JsonPropertyName("required_ruby_version")]
    public List<string> RequiredRubyVersion { get; set; } = [];

    /// <summary>
    /// The RubyGems versions the gem can be installed with. Empty for any.
    /// </summary>
    [JsonPropertyName("required_rubygems_version")]
    public List<string> RequiredRubygemsVersion { get; set; } = [];

    [JsonPropertyName("dependencies")]
    public List<RubyGemsDependency> Dependencies { get; set; } = [];

    /// <summary>
    /// The date of the gemspec, <c>yyyy-MM-dd</c>.
    /// </summary>
    [JsonPropertyName("date")]
    public string Date { get; set; }

    /// <summary>
    /// The version of RubyGems that built the gem.
    /// </summary>
    [JsonPropertyName("rubygems_version")]
    public string RubygemsVersion { get; set; }

    [JsonPropertyName("specification_version")]
    public int? SpecificationVersion { get; set; }

    /// <summary>
    /// The lowercase hex SHA-256 of the gem file, which the compact index lists as its checksum.
    /// </summary>
    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; }

    /// <summary>
    /// The full name of the gem's file, without its extension.
    /// </summary>
    [JsonIgnore]
    public string FullName => RubyGemsFileName.GetFullName(Name, Version, Platform);
}
