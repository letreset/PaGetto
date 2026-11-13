using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PaGetto.Core.Tests.Support;

/// <summary>
/// Builds Go module zips for tests.
/// </summary>
public static class GoModuleZipBuilder
{
    /// <summary>
    /// A module zip with its files in a <c>{module}@{version}/</c> folder, as the go command reads it.
    /// </summary>
    /// <param name="module">The module path.</param>
    /// <param name="version">The version.</param>
    /// <param name="readme">The README.md, or null for none.</param>
    /// <param name="goMod">The go.mod file; by default one that declares the module.</param>
    public static byte[] Create(string module, string version, string readme = null, string goMod = null)
    {
        var folder = $"{module}@{version}/";
        var files = new Dictionary<string, string>
        {
            [folder + "go.mod"] = goMod ?? GoMod(module),
            [folder + "hello.go"] = "package hello\n\nfunc Hello() string { return \"hello\" }\n",
        };

        if (readme != null)
        {
            files[folder + "README.md"] = readme;
        }

        return CreateZip(files);
    }

    /// <summary>
    /// The go.mod file of a module without requirements.
    /// </summary>
    public static string GoMod(string module)
    {
        return $"module {module}\n\ngo 1.22\n";
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
