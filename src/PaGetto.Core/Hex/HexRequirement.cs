using System.Text.Json.Serialization;

namespace PaGetto.Core.Hex;

/// <summary>
/// A dependency of a Hex package version, as the registry lists it.
/// </summary>
public sealed class HexRequirement
{
    /// <summary>
    /// The name of the package depended on.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; init; }

    /// <summary>
    /// The version requirement, e.g. <c>~&gt; 1.4</c>.
    /// </summary>
    [JsonPropertyName("requirement")]
    public string Requirement { get; init; }

    [JsonPropertyName("optional")]
    public bool Optional { get; init; }

    /// <summary>
    /// The OTP application name of the dependency, or null when it is the package name.
    /// </summary>
    [JsonPropertyName("app")]
    public string App { get; init; }

    /// <summary>
    /// The repository the dependency is in, or null when it is the repository of the package.
    /// </summary>
    [JsonPropertyName("repository")]
    public string Repository { get; init; }
}
