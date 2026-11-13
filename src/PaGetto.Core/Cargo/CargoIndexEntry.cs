using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PaGetto.Core.Cargo;

/// <summary>
/// The manifest of a crate version and the line of the index file made from it.
/// <para>
/// The manifest is an index line without <c>cksum</c> and <c>yanked</c>, which the version stores
/// apart, plus the fields of <c>Cargo.toml</c> the web UI shows (<c>description</c>,
/// <c>license</c>, <c>repository</c>, …). A mirrored version's manifest is the mirror's index line.
/// </para>
/// See: https://doc.rust-lang.org/cargo/reference/registry-index.html#json-schema
/// </summary>
public static class CargoIndexEntry
{
    // The fields of the publish metadata kept for the web UI. They are not part of the index.
    private static readonly string[] _packageFields =
    [
        "description", "documentation", "homepage", "repository", "license", "license_file", "keywords", "categories", "authors",
    ];

    /// <summary>
    /// Makes the manifest of a published version from the metadata <c>cargo publish</c> sends.
    /// The name and version are expected to be checked already.
    /// </summary>
    /// <exception cref="InvalidCargoPackageException">The dependencies or features are not in the expected form.</exception>
    public static JsonObject FromPublishMetadata(JsonObject metadata)
    {
        var manifest = new JsonObject
        {
            ["name"] = metadata["name"]?.DeepClone(),
            ["vers"] = metadata["vers"]?.DeepClone(),
            ["deps"] = GetDependencies(metadata),
        };

        // Features that use the syntax of Cargo 1.60 ("dep:name", "name?/feature") go into
        // features2, which older clients skip, and make the entry a version 2 entry.
        var features = new JsonObject();
        var features2 = new JsonObject();
        if (metadata["features"] is JsonObject published)
        {
            foreach (var (feature, value) in published)
            {
                if (value is not JsonArray enables || enables.Any(e => e is not JsonValue v || !v.TryGetValue<string>(out _)))
                {
                    throw new InvalidCargoPackageException($"The feature '{feature}' must be a list of names.");
                }

                var usesNewSyntax = enables.Any(e => e.GetValue<string>() is var text && (text.StartsWith("dep:") || text.Contains("?/")));
                (usesNewSyntax ? features2 : features)[feature] = enables.DeepClone();
            }
        }
        else if (metadata["features"] != null)
        {
            throw new InvalidCargoPackageException("The features of the crate must be an object.");
        }

        manifest["features"] = features;
        if (features2.Count > 0)
        {
            manifest["features2"] = features2;
            manifest["v"] = 2;
        }

        manifest["links"] = metadata["links"]?.DeepClone();

        if (metadata["rust_version"] is JsonValue rustVersion)
        {
            manifest["rust_version"] = rustVersion.DeepClone();
        }

        foreach (var field in _packageFields)
        {
            if (metadata[field] is { } value)
            {
                manifest[field] = value.DeepClone();
            }
        }

        return manifest;
    }

    /// <summary>
    /// Writes the index line of a version: the index fields of its manifest, with its checksum and
    /// whether it is yanked.
    /// </summary>
    public static void Write(Utf8JsonWriter writer, string manifestJson, string cksum, bool yanked)
    {
        using var document = JsonDocument.Parse(manifestJson);
        var manifest = document.RootElement;

        writer.WriteStartObject();
        WriteProperty(writer, manifest, "name");
        WriteProperty(writer, manifest, "vers");

        if (!WriteProperty(writer, manifest, "deps"))
        {
            writer.WriteStartArray("deps");
            writer.WriteEndArray();
        }

        writer.WriteString("cksum", cksum);

        if (!WriteProperty(writer, manifest, "features"))
        {
            writer.WriteStartObject("features");
            writer.WriteEndObject();
        }

        WriteProperty(writer, manifest, "features2");
        writer.WriteBoolean("yanked", yanked);

        if (!WriteProperty(writer, manifest, "links"))
        {
            writer.WriteNull("links");
        }

        WriteProperty(writer, manifest, "v");
        WriteProperty(writer, manifest, "rust_version");
        writer.WriteEndObject();
    }

    private static JsonArray GetDependencies(JsonObject metadata)
    {
        var dependencies = new JsonArray();
        if (metadata["deps"] == null)
        {
            return dependencies;
        }

        if (metadata["deps"] is not JsonArray published)
        {
            throw new InvalidCargoPackageException("The dependencies of the crate must be a list.");
        }

        foreach (var item in published)
        {
            if (item is not JsonObject dependency
                || !TryGetString(dependency, "name", out var name)
                || !CargoPackageName.IsValid(name)
                || !TryGetString(dependency, "version_req", out var versionRequirement))
            {
                throw new InvalidCargoPackageException("Each dependency of the crate must have a valid name and a version requirement.");
            }

            // A dependency renamed in Cargo.toml is listed under its new name, with the crate it
            // stands for as "package".
            var hasRename = TryGetString(dependency, "explicit_name_in_toml", out var rename);

            var entry = new JsonObject
            {
                ["name"] = hasRename ? rename : name,
                ["req"] = versionRequirement,
                ["features"] = dependency["features"]?.DeepClone() ?? new JsonArray(),
                ["optional"] = dependency["optional"]?.DeepClone() ?? false,
                ["default_features"] = dependency["default_features"]?.DeepClone() ?? true,
                ["target"] = dependency["target"]?.DeepClone(),
                ["kind"] = dependency["kind"]?.DeepClone() ?? "normal",
            };

            // Without a registry, the dependency is a crate of this feed.
            if (dependency["registry"] is { } registry)
            {
                entry["registry"] = registry.DeepClone();
            }

            if (hasRename)
            {
                entry["package"] = name;
            }

            dependencies.Add(entry);
        }

        return dependencies;
    }

    private static bool TryGetString(JsonObject item, string property, out string value)
    {
        value = null;
        return item[property] is JsonValue node && node.TryGetValue(out value) && !string.IsNullOrEmpty(value);
    }

    private static bool WriteProperty(Utf8JsonWriter writer, JsonElement manifest, string property)
    {
        if (!manifest.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return false;
        }

        writer.WritePropertyName(property);
        value.WriteTo(writer);
        return true;
    }
}
