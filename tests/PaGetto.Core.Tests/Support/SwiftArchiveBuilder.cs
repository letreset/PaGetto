using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PaGetto.Core.Tests.Support;

/// <summary>
/// Builds Swift package source archives for tests.
/// </summary>
public static class SwiftArchiveBuilder
{
    /// <summary>
    /// The manifest of a library package.
    /// </summary>
    public static string PackageSwift(string name = "LinkedList", string toolsVersion = "5.9")
    {
        return $$"""
            // swift-tools-version:{{toolsVersion}}
            import PackageDescription

            let package = Package(
                name: "{{name}}",
                products: [.library(name: "{{name}}", targets: ["{{name}}"])],
                targets: [.target(name: "{{name}}")]
            )

            """;
    }

    /// <summary>
    /// A source archive as <c>swift package-registry publish</c> writes it: the package in one folder.
    /// </summary>
    /// <param name="folder">The folder the package is in.</param>
    /// <param name="readme">The README.md, or null for none.</param>
    /// <param name="extraFiles">More files of the package, by their path in the folder.</param>
    public static byte[] Create(string folder = "mona.LinkedList", string readme = null, IDictionary<string, string> extraFiles = null)
    {
        var files = new Dictionary<string, string>
        {
            [$"{folder}/Package.swift"] = PackageSwift(),
            [$"{folder}/Sources/LinkedList/LinkedList.swift"] = "public struct LinkedList {}\n",
        };

        if (readme != null)
        {
            files[$"{folder}/README.md"] = readme;
        }

        foreach (var (path, content) in extraFiles ?? new Dictionary<string, string>())
        {
            files[$"{folder}/{path}"] = content;
        }

        return CreateZip(files);
    }

    /// <summary>
    /// A zip of text files, by their path.
    /// </summary>
    public static byte[] CreateZip(IDictionary<string, string> files)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in files)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }
}
