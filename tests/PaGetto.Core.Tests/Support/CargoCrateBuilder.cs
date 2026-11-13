using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;

namespace PaGetto.Core.Tests.Support;

/// <summary>
/// Builds <c>.crate</c> files and the bodies of <c>cargo publish</c> for tests.
/// </summary>
public static class CargoCrateBuilder
{
    /// <summary>
    /// A crate with its files in a <c>{name}-{version}</c> folder, as <c>cargo package</c> writes it.
    /// </summary>
    public static byte[] Create(string name, string version, string readme = null)
    {
        var folder = $"{name}-{version}/";
        var files = new Dictionary<string, string>
        {
            [folder + "Cargo.toml"] = $"[package]\nname = \"{name}\"\nversion = \"{version}\"\nedition = \"2021\"\n",
            [folder + "src/lib.rs"] = "pub fn hello() {}\n",
        };

        if (readme != null)
        {
            files[folder + "README.md"] = readme;
        }

        return PubArchiveBuilder.Create(files).ToArray();
    }

    /// <summary>
    /// The metadata <c>cargo publish</c> sends for a crate without dependencies or features.
    /// </summary>
    public static JsonObject Metadata(string name, string version)
    {
        return new JsonObject
        {
            ["name"] = name,
            ["vers"] = version,
            ["deps"] = new JsonArray(),
            ["features"] = new JsonObject(),
            ["authors"] = new JsonArray(),
            ["description"] = "A test crate.",
            ["documentation"] = null,
            ["homepage"] = null,
            ["readme"] = null,
            ["readme_file"] = null,
            ["keywords"] = new JsonArray(),
            ["categories"] = new JsonArray(),
            ["license"] = "MIT",
            ["license_file"] = null,
            ["repository"] = null,
            ["badges"] = new JsonObject(),
            ["links"] = null,
            ["rust_version"] = null,
        };
    }

    /// <summary>
    /// The body of <c>cargo publish</c>: each part after its length as a 32-bit little-endian number.
    /// </summary>
    public static byte[] PublishBody(JsonObject metadata, byte[] crate)
    {
        var json = Encoding.UTF8.GetBytes(metadata.ToJsonString());

        using var body = new MemoryStream();
        WriteLength(body, json.Length);
        body.Write(json);
        WriteLength(body, crate.Length);
        body.Write(crate);

        return body.ToArray();
    }

    private static void WriteLength(Stream stream, int length)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)length);
        stream.Write(bytes);
    }
}
