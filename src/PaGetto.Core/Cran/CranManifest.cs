using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PaGetto.Core.Cran;

/// <summary>
/// The manifest of a version of an R package, stored as the version's metadata: the fields of its
/// <c>DESCRIPTION</c> file as a JSON object, in the file's order, with the MD5 of the source
/// package as <c>MD5sum</c> (which <c>install.packages</c> checks). The index entry of the version
/// in <c>PACKAGES</c> is made from it.
/// </summary>
public static class CranManifest
{
    public const string PackageField = "Package";
    public const string VersionField = "Version";
    public const string Md5Field = "MD5sum";

    /// <summary>
    /// The fields of a <c>PACKAGES</c> entry, in the order <c>tools::write_PACKAGES</c> writes them.
    /// </summary>
    private static readonly string[] _indexFields =
    [
        PackageField, VersionField, "Priority", "Depends", "Imports", "LinkingTo", "Suggests", "Enhances",
        "License", "License_is_FOSS", "License_restricts_use", "OS_type", "Archs", Md5Field, "NeedsCompilation",
    ];

    /// <summary>
    /// Creates the manifest of a version.
    /// </summary>
    /// <param name="description">The fields of the package's <c>DESCRIPTION</c> file, or of its mirror's index entry.</param>
    /// <param name="md5">The MD5 of the source package, in hexadecimal.</param>
    public static string Create(IEnumerable<KeyValuePair<string, string>> description, string md5)
    {
        var manifest = new JsonObject();
        foreach (var (name, value) in description)
        {
            if (name != Md5Field && !manifest.ContainsKey(name))
            {
                manifest[name] = value;
            }
        }

        manifest[Md5Field] = md5;
        return manifest.ToJsonString();
    }

    /// <summary>
    /// Reads a manifest. Anything that is not a JSON object of strings gives no fields.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Parse(string manifestJson)
    {
        if (string.IsNullOrEmpty(manifestJson))
        {
            return [];
        }

        try
        {
            if (JsonNode.Parse(manifestJson) is not JsonObject manifest)
            {
                return [];
            }

            return manifest
                .Where(p => p.Value is JsonValue value && value.TryGetValue<string>(out _))
                .Select(p => KeyValuePair.Create(p.Key, p.Value!.GetValue<string>()))
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// The fields of the version's entry in <c>PACKAGES</c>.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string>> ToIndexEntry(IReadOnlyList<KeyValuePair<string, string>> fields)
    {
        foreach (var name in _indexFields)
        {
            var value = CranDcf.GetValue(fields, name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return KeyValuePair.Create(name, value);
            }
        }
    }

    /// <summary>
    /// The package name as published, or null if the manifest has none.
    /// </summary>
    public static string GetName(string manifestJson)
    {
        return CranDcf.GetValue(Parse(manifestJson), PackageField);
    }
}
