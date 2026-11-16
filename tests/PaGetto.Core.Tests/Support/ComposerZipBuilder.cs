using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;

namespace PaGetto.Core.Tests.Support;

/// <summary>
/// Builds Composer package zips for tests.
/// </summary>
public static class ComposerZipBuilder
{
    /// <summary>
    /// A package zip with <c>composer.json</c>, a PHP class and optionally a readme.
    /// </summary>
    /// <param name="name">The package name in <c>composer.json</c>.</param>
    /// <param name="folder">The folder the files are in, as GitHub and <c>git archive</c> write them, or null for the root.</param>
    /// <param name="readme">The README.md, or null for none.</param>
    /// <param name="composerJson">The composer.json file; by default <see cref="ComposerJson"/>.</param>
    public static byte[] Create(string name, string folder = null, string readme = null, string composerJson = null)
    {
        var prefix = folder == null ? "" : folder + "/";
        var files = new Dictionary<string, string>
        {
            [prefix + "composer.json"] = composerJson ?? ComposerJson(name),
            [prefix + "src/Hello.php"] = "<?php\n\nnamespace Acme\\Hello;\n\nclass Hello\n{\n    public static function hello(): string\n    {\n        return 'hello';\n    }\n}\n",
        };

        if (readme != null)
        {
            files[prefix + "README.md"] = readme;
        }

        return CreateZip(files);
    }

    /// <summary>
    /// The composer.json file of a library with a PSR-4 autoloader.
    /// </summary>
    /// <param name="name">The package name.</param>
    /// <param name="configure">Changes the file, e.g. to add a version or requirements.</param>
    public static string ComposerJson(string name, Action<JsonObject> configure = null)
    {
        var composerJson = new JsonObject
        {
            ["name"] = name,
            ["description"] = "A test package",
            ["type"] = "library",
            ["license"] = "MIT",
            ["require"] = new JsonObject { ["php"] = ">=8.1" },
            ["autoload"] = new JsonObject { ["psr-4"] = new JsonObject { ["Acme\\Hello\\"] = "src/" } },
        };

        configure?.Invoke(composerJson);

        return composerJson.ToJsonString();
    }

    /// <summary>
    /// A zip of the given files, by their full names in the zip.
    /// </summary>
    public static byte[] CreateZip(IReadOnlyDictionary<string, string> files)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                var entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }
}
