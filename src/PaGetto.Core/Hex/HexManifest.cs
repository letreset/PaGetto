using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PaGetto.Core.Hex;

/// <summary>
/// The manifest of a Hex package version, stored as its <see cref="Entities.ArtifactVersion.MetadataJson"/>:
/// the fields of the package's <c>metadata.config</c> that the registry and the web UI use, its
/// inner checksum, and its retirement.
/// </summary>
public sealed class HexManifest
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [JsonPropertyName("name")]
    public string Name { get; init; }

    [JsonPropertyName("version")]
    public string Version { get; init; }

    [JsonPropertyName("description")]
    public string Description { get; init; }

    /// <summary>
    /// The OTP application name, or null when it is the package name.
    /// </summary>
    [JsonPropertyName("app")]
    public string App { get; init; }

    [JsonPropertyName("licenses")]
    public IReadOnlyList<string> Licenses { get; init; } = [];

    /// <summary>
    /// The links by their names, e.g. <c>GitHub</c>. Only http and https URLs are kept, so the
    /// links are safe to render.
    /// </summary>
    [JsonPropertyName("links")]
    public IReadOnlyDictionary<string, string> Links { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// The build tools of the package, e.g. <c>mix</c> or <c>rebar3</c>.
    /// </summary>
    [JsonPropertyName("build_tools")]
    public IReadOnlyList<string> BuildTools { get; init; } = [];

    /// <summary>
    /// The Elixir versions the package supports, e.g. <c>~&gt; 1.15</c>, or null.
    /// </summary>
    [JsonPropertyName("elixir")]
    public string Elixir { get; init; }

    [JsonPropertyName("requirements")]
    public IReadOnlyList<HexRequirement> Requirements { get; init; } = [];

    /// <summary>
    /// The lowercase hex SHA-256 of the package's version, metadata and contents.
    /// </summary>
    [JsonPropertyName("inner_checksum")]
    public string InnerChecksum { get; init; }

    /// <summary>
    /// The reason and message of the last retirement. The version is retired while it is unlisted.
    /// </summary>
    [JsonPropertyName("retirement")]
    public HexRetirement Retirement { get; init; }

    /// <summary>
    /// Reads the terms of a package's <c>metadata.config</c>.
    /// </summary>
    /// <param name="terms">The terms, as <see cref="ErlangTermText"/> reads them.</param>
    /// <param name="innerChecksum">The lowercase hex inner checksum of the package.</param>
    /// <exception cref="InvalidHexPackageException">The metadata is not valid.</exception>
    public static HexManifest FromMetadata(IReadOnlyList<object> terms, string innerChecksum)
    {
        var metadata = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var term in terms)
        {
            if (term is not object[] { Length: 2 } pair || pair[0] is not string key || !metadata.TryAdd(key, pair[1]))
            {
                throw new InvalidHexPackageException("The metadata.config of the package is not a list of unique {Key, Value} terms.");
            }
        }

        var links = new Dictionary<string, string>(StringComparer.Ordinal);
        var linkPairs = metadata.TryGetValue("links", out var linkValue) && linkValue != null
            ? ToMap(linkValue, "links")
            : [];

        foreach (var (linkName, url) in linkPairs)
        {
            if (url is string text && Uri.TryCreate(text, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                links[linkName] = text;
            }
        }

        return new HexManifest
        {
            Name = GetString(metadata, "name"),
            Version = GetString(metadata, "version"),
            Description = GetString(metadata, "description"),
            App = GetString(metadata, "app"),
            Licenses = GetStrings(metadata, "licenses"),
            Links = links,
            BuildTools = GetStrings(metadata, "build_tools"),
            Elixir = GetString(metadata, "elixir"),
            Requirements = GetRequirements(metadata),
            InnerChecksum = innerChecksum,
        };
    }

    /// <summary>
    /// Reads a stored manifest. Anything that is not a manifest gives an empty one.
    /// </summary>
    public static HexManifest Parse(string json)
    {
        if (!string.IsNullOrEmpty(json))
        {
            try
            {
                return JsonSerializer.Deserialize<HexManifest>(json, _jsonOptions) ?? new HexManifest();
            }
            catch (JsonException)
            {
            }
        }

        return new HexManifest();
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, _jsonOptions);
    }

    /// <summary>
    /// A copy with another retirement.
    /// </summary>
    public HexManifest WithRetirement(HexRetirement retirement)
    {
        return new HexManifest
        {
            Name = Name,
            Version = Version,
            Description = Description,
            App = App,
            Licenses = Licenses,
            Links = Links,
            BuildTools = BuildTools,
            Elixir = Elixir,
            Requirements = Requirements,
            InnerChecksum = InnerChecksum,
            Retirement = retirement,
        };
    }

    /// <summary>
    /// Reads the requirements, which Hex writes in one of two forms: a list of
    /// <c>[{"name", Name}, {"requirement", Requirement}, ...]</c>, or a list of
    /// <c>{Name, [{"requirement", Requirement}, ...]}</c>.
    /// </summary>
    private static List<HexRequirement> GetRequirements(Dictionary<string, object> metadata)
    {
        if (!metadata.TryGetValue("requirements", out var value) || value is null)
        {
            return [];
        }

        if (value is not List<object> items)
        {
            throw Invalid("requirements");
        }

        var requirements = new List<HexRequirement>();
        foreach (var item in items)
        {
            string name;
            Dictionary<string, object> fields;

            if (item is object[] { Length: 2 } named && named[0] is string)
            {
                name = (string)named[0];
                fields = ToMap(named[1], "requirements");
            }
            else
            {
                fields = ToMap(item, "requirements");
                name = fields.GetValueOrDefault("name") as string;
            }

            var requirement = fields.GetValueOrDefault("requirement") as string;
            if (!HexPackageName.IsValid(name) || string.IsNullOrWhiteSpace(requirement))
            {
                throw new InvalidHexPackageException("Each requirement of the package needs a valid package name and a version requirement.");
            }

            var app = fields.GetValueOrDefault("app") as string;
            requirements.Add(new HexRequirement
            {
                Name = name,
                Requirement = requirement,
                Optional = fields.GetValueOrDefault("optional") is true,
                App = string.IsNullOrEmpty(app) || app == name ? null : app,
                Repository = fields.GetValueOrDefault("repository") as string,
            });
        }

        return requirements;
    }

    private static Dictionary<string, object> ToMap(object value, string field)
    {
        var map = new Dictionary<string, object>(StringComparer.Ordinal);
        if (value is not List<object> pairs)
        {
            throw Invalid(field);
        }

        foreach (var pair in pairs)
        {
            if (pair is not object[] { Length: 2 } tuple || tuple[0] is not string key)
            {
                throw Invalid(field);
            }

            map[key] = tuple[1];
        }

        return map;
    }

    private static string GetString(Dictionary<string, object> metadata, string field)
    {
        if (!metadata.TryGetValue(field, out var value) || value is null)
        {
            return null;
        }

        return value as string ?? throw Invalid(field);
    }

    private static List<string> GetStrings(Dictionary<string, object> metadata, string field)
    {
        if (!metadata.TryGetValue(field, out var value) || value is null)
        {
            return [];
        }

        if (value is not List<object> items || items.Any(i => i is not string))
        {
            throw Invalid(field);
        }

        return items.Cast<string>().ToList();
    }

    private static InvalidHexPackageException Invalid(string field)
    {
        return new InvalidHexPackageException($"The {field} field of the package's metadata.config is not valid.");
    }
}
