using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PaGetto.Core.Tests.Support;

/// <summary>
/// Builds pub package archives (gzipped tar files) for tests.
/// </summary>
public static class PubArchiveBuilder
{
    public static MemoryStream Create(string name, string version, string readme = null, string extraPubspec = null)
    {
        var files = new Dictionary<string, string>
        {
            ["lib/" + name + ".dart"] = "void main() {}\n",
            ["pubspec.yaml"] = $"name: {name}\nversion: {version}\ndescription: A test package.\nenvironment:\n  sdk: ^3.0.0\n{extraPubspec}",
        };

        if (readme != null)
        {
            files["README.md"] = readme;
        }

        return Create(files);
    }

    public static MemoryStream Create(IReadOnlyDictionary<string, string> files, TarEntryFormat format = TarEntryFormat.Ustar)
    {
        var output = new MemoryStream();

        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip, format))
        {
            foreach (var (path, content) in files)
            {
                TarEntry entry = format switch
                {
                    TarEntryFormat.V7 => new V7TarEntry(TarEntryType.V7RegularFile, path),
                    TarEntryFormat.Pax => new PaxTarEntry(TarEntryType.RegularFile, path),
                    TarEntryFormat.Gnu => new GnuTarEntry(TarEntryType.RegularFile, path),
                    _ => new UstarTarEntry(TarEntryType.RegularFile, path),
                };

                entry.DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content));
                tar.WriteEntry(entry);
            }
        }

        output.Position = 0;
        return output;
    }
}
