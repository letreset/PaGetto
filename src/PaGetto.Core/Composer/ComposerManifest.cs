using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PaGetto.Core.Composer;

/// <summary>
/// The manifest of a Composer package version: the version as the <c>p2/</c> metadata lists it
/// (its <c>composer.json</c> with <c>name</c>, <c>version</c>, <c>version_normalized</c>,
/// <c>time</c> and <c>dist</c>), without the URL of the dist, which depends on the request.
/// See: https://getcomposer.org/doc/05-repositories.md#composer
/// </summary>
public static class ComposerManifest
{
    /// <summary>
    /// The type of the dists this server serves.
    /// </summary>
    public const string ZipDistType = "zip";

    /// <summary>
    /// The manifest of an uploaded version: its <c>composer.json</c>, with the name and version it
    /// was uploaded as. The zip's SHA-1 is its reference, so a replaced version has a new one and
    /// Composer installs it again.
    /// </summary>
    public static string Create(JsonObject composerJson, string name, ComposerVersion version, DateTime time, string sha1)
    {
        var manifest = (JsonObject)composerJson.DeepClone();
        manifest["name"] = name;
        manifest["version"] = version.Text;
        manifest["version_normalized"] = version.Normalized;
        manifest["time"] = time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'+00:00'", CultureInfo.InvariantCulture);
        manifest["dist"] = CreateDist(sha1, sha1);

        return manifest.ToJsonString();
    }

    /// <summary>
    /// The manifest of a version stored from a mirror: the mirror's metadata of the version, with a
    /// dist of the stored zip. The mirror's reference is kept, so lock files stay valid.
    /// </summary>
    public static string CreateMirrored(JsonObject upstreamVersion, string name, string sha1)
    {
        var manifest = (JsonObject)upstreamVersion.DeepClone();
        manifest["name"] = name;
        manifest["dist"] = CreateDist(GetDistReference(upstreamVersion) ?? sha1, sha1);

        return manifest.ToJsonString();
    }

    /// <summary>
    /// The version as the <c>p2/</c> metadata lists it.
    /// </summary>
    /// <param name="manifestJson">The stored manifest.</param>
    /// <param name="distBaseUrl">The absolute URL the paths of the feed's zips are under.</param>
    /// <param name="name">The package name, lowercased.</param>
    /// <param name="version">The version as stored.</param>
    /// <returns>The version, or null if the manifest is not a JSON object.</returns>
    public static JsonObject ToPackage(string manifestJson, string distBaseUrl, string name, string version)
    {
        var package = Parse(manifestJson);
        if (package?["dist"] is JsonObject dist)
        {
            dist["url"] = $"{distBaseUrl}/{GetDistPath(name, version, GetDistReference(package))}";
        }

        return package;
    }

    /// <summary>
    /// The path of a version's zip below the feed's dist URL: <c>{vendor}/{package}/{version}/{reference}.zip</c>.
    /// The reference is only there so the URL changes when the zip does, since Composer caches zips by URL.
    /// </summary>
    public static string GetDistPath(string name, string version, string reference)
    {
        return $"{name}/{Uri.EscapeDataString(version)}/{Uri.EscapeDataString(reference ?? version)}.zip";
    }

    /// <summary>
    /// The <c>dist.reference</c> of a version, or null.
    /// </summary>
    public static string GetDistReference(JsonObject version)
    {
        return version?["dist"] is JsonObject dist && dist["reference"] is JsonValue value && value.TryGetValue<string>(out var reference)
            && !string.IsNullOrEmpty(reference)
            ? reference
            : null;
    }

    /// <summary>
    /// Reads a manifest.
    /// </summary>
    /// <returns>The manifest, or null if it is not a JSON object.</returns>
    public static JsonObject Parse(string manifestJson)
    {
        try
        {
            return string.IsNullOrEmpty(manifestJson) ? null : JsonNode.Parse(manifestJson) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonObject CreateDist(string reference, string sha1)
    {
        return new JsonObject
        {
            ["type"] = ZipDistType,
            ["reference"] = reference,
            ["shasum"] = sha1,
        };
    }
}
