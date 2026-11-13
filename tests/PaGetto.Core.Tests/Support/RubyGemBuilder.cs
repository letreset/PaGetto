using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace PaGetto.Core.Tests.Support;

/// <summary>
/// Builds <c>.gem</c> files for tests, laid out as <c>gem build</c> writes them: a tar archive with
/// the gemspec as gzipped YAML in <c>metadata.gz</c> and the gem's files in <c>data.tar.gz</c>.
/// </summary>
public static class RubyGemBuilder
{
    /// <summary>
    /// A gem with one runtime dependency on rake, unless other dependencies are given.
    /// </summary>
    /// <param name="name">The gem name.</param>
    /// <param name="version">The version.</param>
    /// <param name="platform">The platform, or null for <c>ruby</c>.</param>
    /// <param name="readme">The content of README.md, or null for none.</param>
    /// <param name="dependencies">The dependencies: name, requirement such as <c>~&gt; 1.0</c>, and <c>runtime</c> or <c>development</c>.</param>
    /// <param name="requiredRubyVersion">The required Ruby version such as <c>&gt;= 3.0</c>, or null for any.</param>
    public static byte[] Create(
        string name,
        string version,
        string platform = null,
        string readme = null,
        IReadOnlyList<(string Name, string Requirement, string Type)> dependencies = null,
        string requiredRubyVersion = ">= 3.0")
    {
        return CreateFromGemspec(Gemspec(name, version, platform, dependencies, requiredRubyVersion), name, readme);
    }

    /// <summary>
    /// A gem with the given gemspec YAML.
    /// </summary>
    public static byte[] CreateFromGemspec(string gemspecYaml, string name = "gem", string readme = null)
    {
        var files = new Dictionary<string, string> { [$"lib/{name}.rb"] = "module Gem; end\n" };
        if (readme != null)
        {
            files["README.md"] = readme;
        }

        return CreateTar(new Dictionary<string, byte[]>
        {
            ["metadata.gz"] = Gzip(Encoding.UTF8.GetBytes(gemspecYaml)),
            ["data.tar.gz"] = PubArchiveBuilder.Create(files).ToArray(),
            ["checksums.yaml.gz"] = Gzip("---\n"u8.ToArray()),
        });
    }

    /// <summary>
    /// A gemspec as RubyGems writes it to <c>metadata.gz</c>, with its Ruby object tags.
    /// </summary>
    public static string Gemspec(
        string name,
        string version,
        string platform = null,
        IReadOnlyList<(string Name, string Requirement, string Type)> dependencies = null,
        string requiredRubyVersion = ">= 3.0")
    {
        dependencies ??= [("rake", "~> 13.0", "runtime")];

        var yaml = new StringBuilder();
        yaml.Append($"""
            --- !ruby/object:Gem::Specification
            name: {name}
            version: !ruby/object:Gem::Version
              version: {version}
            platform: {platform ?? "ruby"}
            authors:
            - Test Author
            autorequire:
            bindir: bin
            cert_chain: []
            date: 2020-01-01 00:00:00.000000000 Z
            dependencies:

            """);

        foreach (var dependency in dependencies)
        {
            var requirement = Requirement(dependency.Requirement, "  ");
            yaml.Append($"""
                - !ruby/object:Gem::Dependency
                  name: {dependency.Name}
                  requirement: {requirement}
                  type: :{dependency.Type}
                  prerelease: false
                  version_requirements: {requirement}

                """);
        }

        yaml.Append($"""
            description: A longer description of {name}.
            email:
            - test@example.com
            executables: []
            extensions: []
            extra_rdoc_files: []
            files:
            - lib/{name}.rb
            homepage: https://example.com/{name}
            licenses:
            - MIT
            metadata:
              source_code_uri: https://example.com/{name}/source
            post_install_message:
            rdoc_options: []
            require_paths:
            - lib
            required_ruby_version: {Requirement(requiredRubyVersion ?? ">= 0", "")}
            required_rubygems_version: {Requirement(">= 0", "")}
            requirements: []
            rubygems_version: 3.5.3
            signing_key:
            specification_version: 4
            summary: A test gem.
            test_files: []

            """);

        return yaml.ToString();
    }

    /// <summary>
    /// A tar archive (not compressed) of files.
    /// </summary>
    public static byte[] CreateTar(IReadOnlyDictionary<string, byte[]> files)
    {
        using var output = new MemoryStream();
        using (var tar = new TarWriter(output, TarEntryFormat.Ustar, leaveOpen: true))
        {
            foreach (var (path, content) in files)
            {
                tar.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, path) { DataStream = new MemoryStream(content) });
            }
        }

        return output.ToArray();
    }

    public static byte[] Gzip(byte[] content)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(content);
        }

        return output.ToArray();
    }

    /// <summary>
    /// A <c>!ruby/object:Gem::Requirement</c> of comma-separated parts, as YAML that continues the
    /// line of its key and is indented by <paramref name="indent"/>.
    /// </summary>
    private static string Requirement(string requirement, string indent)
    {
        var yaml = new StringBuilder("!ruby/object:Gem::Requirement\n");
        yaml.Append(indent).Append("  requirements:");

        foreach (var part in requirement.Split(',', StringSplitOptions.TrimEntries))
        {
            var (op, version) = part.Split(' ', 2) is [var o, var v] ? (o, v) : ("=", part);
            yaml.Append('\n').Append(indent).Append($"  - - \"{op}\"");
            yaml.Append('\n').Append(indent).Append("    - !ruby/object:Gem::Version");
            yaml.Append('\n').Append(indent).Append($"      version: '{version}'");
        }

        return yaml.ToString();
    }
}
