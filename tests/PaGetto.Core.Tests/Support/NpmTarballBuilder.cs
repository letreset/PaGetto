using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;

namespace PaGetto.Core.Tests.Support;

/// <summary>
/// Builds npm package tarballs (gzipped tar files with the files in <c>package/</c>) for tests.
/// </summary>
public static class NpmTarballBuilder
{
    public static byte[] Create(string name, string version, string readme = null, JsonObject extraManifest = null)
    {
        return Create(CreateManifest(name, version, extraManifest), readme);
    }

    public static byte[] Create(JsonObject manifest, string readme = null)
    {
        var files = new Dictionary<string, string>
        {
            ["package/package.json"] = manifest.ToJsonString(),
            ["package/index.js"] = "module.exports = {};\n",
        };

        if (readme != null)
        {
            files["package/README.md"] = readme;
        }

        using var tarball = PubArchiveBuilder.Create(files);
        return tarball.ToArray();
    }

    /// <summary>
    /// A package.json with a description, and the given fields on top.
    /// </summary>
    public static JsonObject CreateManifest(string name, string version, JsonObject extraManifest = null)
    {
        var manifest = new JsonObject
        {
            ["name"] = name,
            ["version"] = version,
            ["description"] = "A test package.",
            ["main"] = "index.js",
        };

        foreach (var (key, value) in extraManifest ?? new JsonObject())
        {
            manifest[key] = value?.DeepClone();
        }

        return manifest;
    }

    public static MemoryStream CreateStream(string name, string version, string readme = null)
    {
        return new MemoryStream(Create(name, version, readme));
    }
}
