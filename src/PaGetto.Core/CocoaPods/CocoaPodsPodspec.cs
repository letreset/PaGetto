using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PaGetto.Core.CocoaPods;

/// <summary>
/// Podspecs in their JSON form (<c>{name}.podspec.json</c>), which CocoaPods reads from a CDN spec
/// repository and <c>pod ipc spec</c> writes from a Ruby podspec. A version's podspec is its manifest,
/// stored as published; when the version's source archive is stored in the feed, its <c>source</c>
/// points at the archive, and the archive's URL is added when the podspec is served.
/// See: https://guides.cocoapods.org/syntax/podspec.html
/// </summary>
public static class CocoaPodsPodspec
{
    // The archives CocoaPods downloads with curl and unpacks on any platform, by file name ending.
    // A dmg needs macOS's hdiutil, so it is not stored.
    private static readonly (string Ending, string Type)[] _archiveTypes =
    [
        (".zip", "zip"),
        (".tar.gz", "tgz"),
        (".tgz", "tgz"),
        (".tar.bz2", "tbz"),
        (".tbz", "tbz"),
        (".tar.xz", "txz"),
        (".txz", "txz"),
        (".tar", "tar"),
    ];

    /// <summary>
    /// Reads a published podspec and checks its name and version.
    /// </summary>
    /// <exception cref="InvalidPodspecException">The podspec is not a JSON object with a valid name and version.</exception>
    public static JsonObject Read(string json)
    {
        JsonObject podspec;
        try
        {
            podspec = JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException e)
        {
            throw new InvalidPodspecException($"The podspec is not valid JSON: {e.Message} Convert a Ruby podspec with: pod ipc spec Name.podspec > Name.podspec.json", e);
        }

        if (podspec == null)
        {
            throw new InvalidPodspecException("The podspec is not a JSON object.");
        }

        var name = GetString(podspec, "name");
        if (!CocoaPodsPodName.IsValid(name))
        {
            throw new InvalidPodspecException(
                $"{name ?? "(none)"} is not a pod name this server accepts: ASCII letters, digits and _ + - . ! @ ( ), " +
                $"not starting with a dot, and at most {CocoaPodsPodName.MaxLength} characters.");
        }

        var version = GetString(podspec, "version");
        if (!CocoaPodsVersion.TryParse(version, out _))
        {
            throw new InvalidPodspecException($"{version ?? "(none)"} is not a version CocoaPods accepts, such as 1.0.0 or 2.0.0-beta.1.");
        }

        return podspec;
    }

    public static string GetName(JsonObject podspec)
    {
        return GetString(podspec, "name");
    }

    public static string GetVersion(JsonObject podspec)
    {
        return GetString(podspec, "version");
    }

    /// <summary>
    /// The podspec's summary, or its description when it has none.
    /// </summary>
    public static string GetSummary(JsonObject podspec)
    {
        return GetString(podspec, "summary") ?? GetString(podspec, "description");
    }

    /// <summary>
    /// The type of a source archive, as CocoaPods names it in a podspec's <c>source</c>, from its file name.
    /// </summary>
    /// <returns>False if CocoaPods can't unpack it on every platform.</returns>
    public static bool TryGetArchiveType(string fileName, out string type)
    {
        type = _archiveTypes
            .Where(t => fileName?.EndsWith(t.Ending, StringComparison.OrdinalIgnoreCase) == true)
            .Select(t => t.Type)
            .FirstOrDefault();

        return type != null;
    }

    /// <summary>
    /// Points the source of a podspec at an archive stored in the feed: its type and SHA-256, which
    /// CocoaPods checks, and the <c>flatten</c> option of the published source, if any. The URL is
    /// added by <see cref="AddSourceUrl"/>.
    /// </summary>
    public static void SetStoredSource(JsonObject podspec, string type, string sha256)
    {
        var source = new JsonObject
        {
            ["type"] = type,
            ["sha256"] = sha256,
        };

        if ((podspec["source"] as JsonObject)?["flatten"] is JsonValue flatten)
        {
            source["flatten"] = flatten.DeepClone();
        }

        podspec["source"] = source;
    }

    /// <summary>
    /// The podspec of a version whose source archive is stored in the feed, as CocoaPods downloads it:
    /// with the archive's URL as its <c>http</c> source.
    /// </summary>
    public static string AddSourceUrl(string podspecJson, string url)
    {
        var podspec = (JsonObject)JsonNode.Parse(podspecJson)!;
        var source = new JsonObject { ["http"] = url };
        foreach (var (key, value) in (podspec["source"] as JsonObject) ?? new JsonObject())
        {
            if (key != "http")
            {
                source[key] = value?.DeepClone();
            }
        }

        podspec["source"] = source;
        return podspec.ToJsonString();
    }

    /// <summary>
    /// The type of the source archive stored for a version, from its stored podspec.
    /// </summary>
    /// <returns>The type, or null.</returns>
    public static string GetStoredArchiveType(string podspecJson)
    {
        try
        {
            return GetString((JsonNode.Parse(podspecJson) as JsonObject)?["source"] as JsonObject, "type");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The file name a stored source archive is served under, e.g. <c>MyLib-1.0.0.zip</c>.
    /// </summary>
    public static string GetArchiveFileName(string name, string version, string type)
    {
        return $"{name}-{version}.{type}";
    }

    private static string GetString(JsonObject item, string property)
    {
        return item?[property] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;
    }
}
