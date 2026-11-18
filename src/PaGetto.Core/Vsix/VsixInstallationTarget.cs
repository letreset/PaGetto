using System.Text.Json.Serialization;

namespace PaGetto.Core.Vsix;

/// <summary>
/// A Visual Studio product an extension installs into: an <c>InstallationTarget</c> of its manifest.
/// </summary>
public sealed class VsixInstallationTarget
{
    /// <summary>
    /// The product, e.g. <c>Microsoft.VisualStudio.Community</c>.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; }

    /// <summary>
    /// The product versions, as an interval, e.g. <c>[17.0,18.0)</c>.
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; init; }

    /// <summary>
    /// The processor architecture of the product, e.g. <c>amd64</c> or <c>arm64</c>, or null.
    /// </summary>
    [JsonPropertyName("productArchitecture")]
    public string ProductArchitecture { get; init; }
}
