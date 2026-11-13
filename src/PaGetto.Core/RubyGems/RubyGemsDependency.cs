using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PaGetto.Core.RubyGems;

/// <summary>
/// A dependency of a gem, as in its gemspec.
/// </summary>
public sealed class RubyGemsDependency
{
    /// <summary>
    /// The type of the dependencies installed with a gem.
    /// </summary>
    public const string RuntimeType = "runtime";

    /// <summary>
    /// The type of the dependencies only its developers need.
    /// </summary>
    public const string DevelopmentType = "development";

    [JsonPropertyName("name")]
    public string Name { get; set; }

    /// <summary>
    /// The version requirements, each an operator and a version, e.g. <c>~&gt; 1.0</c>.
    /// </summary>
    [JsonPropertyName("requirement")]
    public List<string> Requirement { get; set; } = [];

    /// <summary>
    /// <see cref="RuntimeType"/> or <see cref="DevelopmentType"/>.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = RuntimeType;
}
