using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace PaGetto.Core.RubyGems;

/// <summary>
/// Writes the <c>quick/Marshal.4.8/{gem}.gemspec.rz</c> of a gem: its <c>Gem::Specification</c> as
/// Ruby Marshal data, compressed with zlib. <c>gem install</c> reads it for each gem it installs
/// from a server with a compact index. It is written as <c>Gem::Specification#_dump</c> writes it,
/// with the fields <c>Gem::Specification._load</c> reads, in the classes <c>Gem::SafeMarshal</c>
/// allows.
/// </summary>
public static class RubyGemsQuickSpec
{
    // The newest gemspec format, the one _load reads all fields of.
    private const int SpecificationVersion = 4;

    /// <summary>
    /// The compressed Marshal data of a gemspec.
    /// </summary>
    public static byte[] Create(RubyGemsSpecification specification)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(Marshal(specification));
        }

        return compressed.ToArray();
    }

    /// <summary>
    /// The Marshal data of a gemspec, not compressed.
    /// </summary>
    public static byte[] Marshal(RubyGemsSpecification specification)
    {
        var writer = new RubyMarshalWriter();
        writer.WriteUserDefined("Gem::Specification", Dump(specification));
        return writer.ToArray();
    }

    /// <summary>
    /// What <c>Gem::Specification#_dump</c> returns: the Marshal data of an array of its fields.
    /// </summary>
    private static byte[] Dump(RubyGemsSpecification specification)
    {
        var platform = RubyGemsFileName.IsRubyPlatform(specification.Platform) ? RubyGemsFileName.RubyPlatform : specification.Platform;

        var writer = new RubyMarshalWriter();
        writer.WriteArrayStart(19);
        writer.WriteString(specification.RubygemsVersion ?? "0");
        writer.WriteInteger(specification.SpecificationVersion is > 0 and <= SpecificationVersion ? specification.SpecificationVersion.Value : SpecificationVersion);
        writer.WriteString(specification.Name);
        WriteVersion(writer, specification.Version);

        // Gem::Specification#date= takes a yyyy-MM-dd string, and today's date for nil.
        writer.WriteString(specification.Date);
        writer.WriteString(specification.Summary);
        WriteRequirement(writer, specification.RequiredRubyVersion);
        WriteRequirement(writer, specification.RequiredRubygemsVersion);

        // The original platform, which _load passes to Gem::Specification#platform=.
        writer.WriteString(platform);

        writer.WriteArrayStart(specification.Dependencies.Count);
        foreach (var dependency in specification.Dependencies)
        {
            writer.WriteObjectStart("Gem::Dependency", 4);
            writer.WriteSymbol("@name");
            writer.WriteString(dependency.Name);
            writer.WriteSymbol("@requirement");
            WriteRequirement(writer, dependency.Requirement);
            writer.WriteSymbol("@type");
            writer.WriteSymbol(dependency.Type == RubyGemsDependency.DevelopmentType ? RubyGemsDependency.DevelopmentType : RubyGemsDependency.RuntimeType);
            writer.WriteSymbol("@prerelease");
            writer.WriteBoolean(false);
        }

        // The rubyforge_project, which is no longer read.
        writer.WriteString(string.Empty);
        WriteStrings(writer, specification.Email);
        WriteStrings(writer, specification.Authors);
        writer.WriteString(specification.Description);
        writer.WriteString(specification.Homepage);

        // has_rdoc, which is no longer read.
        writer.WriteBoolean(true);

        // The platform; its text is what file names are made of.
        writer.WriteString(platform);
        WriteStrings(writer, specification.Licenses);

        writer.WriteHashStart(specification.Metadata.Count);
        foreach (var (key, value) in specification.Metadata.OrderBy(m => m.Key, System.StringComparer.Ordinal))
        {
            writer.WriteString(key);
            writer.WriteString(value);
        }

        return writer.ToArray();
    }

    /// <summary>
    /// A <c>Gem::Version</c>, whose <c>marshal_dump</c> is an array of its text.
    /// </summary>
    private static void WriteVersion(RubyMarshalWriter writer, string version)
    {
        writer.WriteUserMarshalStart("Gem::Version");
        writer.WriteArrayStart(1);
        writer.WriteString(version);
    }

    /// <summary>
    /// A <c>Gem::Requirement</c>, whose <c>marshal_dump</c> is an array of its pairs of an operator
    /// and a <c>Gem::Version</c>. No requirement is <c>&gt;= 0</c>.
    /// </summary>
    private static void WriteRequirement(RubyMarshalWriter writer, List<string> requirement)
    {
        List<string> pairs = requirement.Count == 0 ? [">= 0"] : requirement;

        writer.WriteUserMarshalStart("Gem::Requirement");
        writer.WriteArrayStart(1);
        writer.WriteArrayStart(pairs.Count);
        foreach (var pair in pairs)
        {
            var space = pair.IndexOf(' ');
            writer.WriteArrayStart(2);
            writer.WriteString(space < 0 ? "=" : pair[..space]);
            WriteVersion(writer, space < 0 ? pair : pair[(space + 1)..]);
        }
    }

    private static void WriteStrings(RubyMarshalWriter writer, List<string> values)
    {
        writer.WriteArrayStart(values.Count);
        foreach (var value in values)
        {
            writer.WriteString(value);
        }
    }
}
