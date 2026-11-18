using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PaGetto.Core.Vsix;

/// <summary>
/// The manifest of a version of a Visual Studio extension, stored as its
/// <see cref="Entities.ArtifactVersion.MetadataJson"/>: the parts of the extension's
/// <c>extension.vsixmanifest</c> that the gallery feed and the web UI use. It is also made from an
/// entry of a mirror's gallery feed, which has less.
/// </summary>
public sealed class VsixManifest
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// The identifier as published.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; init; }

    /// <summary>
    /// The version, without leading zeros (see <see cref="VsixVersion"/>).
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; init; }

    /// <summary>
    /// The publisher's name as the extension shows it, e.g. <c>Contoso</c>.
    /// </summary>
    [JsonPropertyName("publisher")]
    public string Publisher { get; init; }

    [JsonPropertyName("language")]
    public string Language { get; init; }

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; }

    [JsonPropertyName("description")]
    public string Description { get; init; }

    /// <summary>
    /// A link to more information, as written. It is shown only when it is an http or https URL
    /// (see <see cref="GetLink"/>).
    /// </summary>
    [JsonPropertyName("moreInfo")]
    public string MoreInfo { get; init; }

    [JsonPropertyName("gettingStartedGuide")]
    public string GettingStartedGuide { get; init; }

    [JsonPropertyName("releaseNotes")]
    public string ReleaseNotes { get; init; }

    /// <summary>
    /// The path of the icon in the extension, or null if it has none that this server serves.
    /// </summary>
    [JsonPropertyName("icon")]
    public string Icon { get; init; }

    /// <summary>
    /// The path of the preview image in the extension, or null if it has none that this server serves.
    /// </summary>
    [JsonPropertyName("previewImage")]
    public string PreviewImage { get; init; }

    [JsonPropertyName("tags")]
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>
    /// Whether the publisher marked the extension as a preview.
    /// </summary>
    [JsonPropertyName("preview")]
    public bool Preview { get; init; }

    /// <summary>
    /// The Visual Studio products and versions the extension installs into.
    /// </summary>
    [JsonPropertyName("installationTargets")]
    public IReadOnlyList<VsixInstallationTarget> InstallationTargets { get; init; } = [];

    /// <summary>
    /// The other extensions the extension needs.
    /// </summary>
    [JsonPropertyName("dependencies")]
    public IReadOnlyList<VsixDependency> Dependencies { get; init; } = [];

    /// <summary>
    /// Reads a stored manifest. Anything that is not a manifest gives an empty one.
    /// </summary>
    public static VsixManifest Parse(string json)
    {
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                return JsonSerializer.Deserialize<VsixManifest>(json, _jsonOptions) ?? new VsixManifest();
            }
            catch (JsonException)
            {
            }
        }

        return new VsixManifest();
    }

    /// <summary>
    /// A link of the manifest when it is an absolute http or https URL, which is safe to render;
    /// otherwise null, e.g. for release notes that are a file in the extension.
    /// </summary>
    public static string GetLink(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri.AbsoluteUri
            : null;
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, _jsonOptions);
    }
}
