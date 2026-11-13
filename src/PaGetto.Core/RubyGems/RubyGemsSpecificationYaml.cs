using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace PaGetto.Core.RubyGems;

/// <summary>
/// Reads a gemspec as RubyGems writes it to the <c>metadata.gz</c> of a gem: YAML with Ruby object
/// tags (<c>!ruby/object:Gem::Specification</c>, <c>Gem::Version</c>, <c>Gem::Requirement</c> and
/// <c>Gem::Dependency</c>), which are read as the mappings they are.
/// </summary>
public static class RubyGemsSpecificationYaml
{
    // Aliases can make a small document refer to a huge list many times, so what is read is bounded.
    private const int MaxItems = 100_000;

    /// <summary>
    /// Parses a gemspec. Only the name, version and platform are checked; the server checks them
    /// again for what it needs.
    /// </summary>
    /// <exception cref="InvalidRubyGemException">The text is not a gemspec.</exception>
    public static RubyGemsSpecification Parse(string yaml)
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException e)
        {
            throw new InvalidRubyGemException("The gemspec in metadata.gz is not valid YAML: " + e.Message, e);
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw new InvalidRubyGemException("The gemspec in metadata.gz must be a mapping.");
        }

        var reader = new Reader();
        var specification = new RubyGemsSpecification
        {
            Name = Scalar(Child(root, "name")),
            Version = Reader.Version(Child(root, "version")),
            Platform = Scalar(Child(root, "platform")) ?? RubyGemsFileName.RubyPlatform,
            Summary = Scalar(Child(root, "summary")),
            Description = Scalar(Child(root, "description")),
            Authors = reader.Strings(Child(root, "authors")),
            Email = reader.Strings(Child(root, "email")),
            Homepage = Scalar(Child(root, "homepage")),
            Licenses = reader.Strings(Child(root, "licenses")),
            Metadata = reader.Metadata(Child(root, "metadata")),
            RequiredRubyVersion = reader.Requirement(Child(root, "required_ruby_version")),
            RequiredRubygemsVersion = reader.Requirement(Child(root, "required_rubygems_version")),
            Dependencies = reader.Dependencies(Child(root, "dependencies")),
            Date = Date(Scalar(Child(root, "date"))),
            RubygemsVersion = Scalar(Child(root, "rubygems_version")),
            SpecificationVersion = int.TryParse(Scalar(Child(root, "specification_version")), NumberStyles.None, CultureInfo.InvariantCulture, out var specVersion)
                ? specVersion
                : null,
        };

        if (string.IsNullOrEmpty(specification.Name) || string.IsNullOrEmpty(specification.Version))
        {
            throw new InvalidRubyGemException("The gemspec in metadata.gz has no name or version.");
        }

        return specification;
    }

    private static YamlNode Child(YamlNode node, string key)
    {
        return node is YamlMappingNode mapping && mapping.Children.TryGetValue(new YamlScalarNode(key), out var child)
            ? child
            : null;
    }

    /// <summary>
    /// The text of a scalar, or null for a missing or null one. A <c>!binary</c> scalar, which old
    /// versions of RubyGems wrote for text that was not ASCII, is decoded.
    /// </summary>
    private static string Scalar(YamlNode node)
    {
        if (node is not YamlScalarNode { Value: { } value } scalar)
        {
            return null;
        }

        var tag = scalar.Tag.IsEmpty ? null : scalar.Tag.Value;
        if (tag is "!binary" or "tag:yaml.org,2002:binary")
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(value));
            }
            catch (FormatException)
            {
                return null;
            }
        }

        // A plain ~ or nothing is null; a quoted one is text.
        if (scalar.Style == ScalarStyle.Plain && value is "" or "~" or "null")
        {
            return null;
        }

        return value;
    }

    /// <summary>
    /// The date of a gemspec is a timestamp, e.g. <c>2024-01-15 00:00:00.000000000 Z</c>. Its day is kept.
    /// </summary>
    private static string Date(string value)
    {
        return value != null && value.Length >= 10 && DateTime.TryParseExact(value[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? value[..10]
            : null;
    }

    private sealed class Reader
    {
        private int _items;

        public static string Version(YamlNode node)
        {
            // !ruby/object:Gem::Version with its version, or plain text.
            return Scalar(node is YamlMappingNode ? Child(node, "version") : node);
        }

        public List<string> Strings(YamlNode node)
        {
            if (node is YamlSequenceNode sequence)
            {
                return sequence.Children.Select(Count).Select(Scalar).Where(s => !string.IsNullOrEmpty(s)).ToList();
            }

            var text = Scalar(node);
            return string.IsNullOrEmpty(text) ? [] : [text];
        }

        public Dictionary<string, string> Metadata(YamlNode node)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (node is not YamlMappingNode mapping)
            {
                return result;
            }

            foreach (var (key, value) in mapping.Children)
            {
                Count(key);
                if (Scalar(key) is { } name && Scalar(value) is { } text)
                {
                    result[name] = text;
                }
            }

            return result;
        }

        /// <summary>
        /// A <c>!ruby/object:Gem::Requirement</c>: its <c>requirements</c> are pairs of an operator
        /// and a <c>Gem::Version</c>. <c>&gt;= 0</c>, which any version meets, is left out.
        /// </summary>
        public List<string> Requirement(YamlNode node)
        {
            var result = new List<string>();
            if (Child(node, "requirements") is not YamlSequenceNode requirements)
            {
                return result;
            }

            foreach (var pair in requirements.Children.Select(Count))
            {
                if (pair is not YamlSequenceNode { Children.Count: 2 } items)
                {
                    throw new InvalidRubyGemException("A version requirement in the gemspec is not an operator and a version.");
                }

                var op = Scalar(items.Children[0]);
                var version = Version(items.Children[1]);
                if (string.IsNullOrEmpty(op) || string.IsNullOrEmpty(version))
                {
                    throw new InvalidRubyGemException("A version requirement in the gemspec is not an operator and a version.");
                }

                var requirement = $"{op} {version}";
                if (requirement != ">= 0")
                {
                    result.Add(requirement);
                }
            }

            return result;
        }

        public List<RubyGemsDependency> Dependencies(YamlNode node)
        {
            var result = new List<RubyGemsDependency>();
            if (node is not YamlSequenceNode sequence)
            {
                return result;
            }

            foreach (var item in sequence.Children.Select(Count))
            {
                var name = Scalar(Child(item, "name"));
                if (string.IsNullOrEmpty(name))
                {
                    throw new InvalidRubyGemException("A dependency in the gemspec has no name.");
                }

                // Old gemspecs only have version_requirements.
                var requirement = Child(item, "requirement") ?? Child(item, "version_requirements");

                // A type is a Ruby symbol, written :runtime.
                var type = Scalar(Child(item, "type"))?.TrimStart(':');

                result.Add(new RubyGemsDependency
                {
                    Name = name,
                    Requirement = Requirement(requirement),
                    Type = type == RubyGemsDependency.DevelopmentType ? RubyGemsDependency.DevelopmentType : RubyGemsDependency.RuntimeType,
                });
            }

            return result;
        }

        private YamlNode Count(YamlNode node)
        {
            if (++_items > MaxItems)
            {
                throw new InvalidRubyGemException("The gemspec in metadata.gz is too large.");
            }

            return node;
        }
    }
}
