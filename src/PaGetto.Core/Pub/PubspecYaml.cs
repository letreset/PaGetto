using System;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace PaGetto.Core.Pub;

/// <summary>
/// Converts a <c>pubspec.yaml</c> to the JSON object pub servers return as a version's <c>pubspec</c>.
/// </summary>
public static partial class PubspecYaml
{
    // Aliases can make a small document expand to a huge one, so the output is bounded.
    private const int MaxNodes = 50_000;
    private const int MaxDepth = 64;

    /// <summary>
    /// Parses the YAML and converts it to JSON, resolving plain scalars as the YAML 1.2 core schema does.
    /// </summary>
    /// <exception cref="InvalidPubArchiveException">The text is not YAML, or its root is not a mapping.</exception>
    public static JsonObject ToJson(string yaml)
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(yaml));
        }
        catch (YamlException e)
        {
            throw new InvalidPubArchiveException("pubspec.yaml is not valid YAML: " + e.Message, e);
        }

        if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root)
        {
            throw new InvalidPubArchiveException("pubspec.yaml must contain a mapping.");
        }

        var nodes = 0;
        return (JsonObject)Convert(root, depth: 0, ref nodes);
    }

    private static JsonNode Convert(YamlNode node, int depth, ref int nodes)
    {
        if (++nodes > MaxNodes || depth > MaxDepth)
        {
            throw new InvalidPubArchiveException("pubspec.yaml is too large or too deeply nested.");
        }

        switch (node)
        {
            case YamlMappingNode mapping:
                var result = new JsonObject();
                foreach (var (key, value) in mapping.Children)
                {
                    if (key is not YamlScalarNode { Value: not null } scalarKey)
                    {
                        throw new InvalidPubArchiveException("pubspec.yaml must only use text as mapping keys.");
                    }

                    result[scalarKey.Value] = Convert(value, depth + 1, ref nodes);
                }

                return result;

            case YamlSequenceNode sequence:
                var array = new JsonArray();
                foreach (var item in sequence.Children)
                {
                    array.Add(Convert(item, depth + 1, ref nodes));
                }

                return array;

            case YamlScalarNode scalar:
                return ConvertScalar(scalar);

            default:
                throw new InvalidPubArchiveException("pubspec.yaml contains an unsupported YAML node.");
        }
    }

    private static JsonValue ConvertScalar(YamlScalarNode scalar)
    {
        var value = scalar.Value ?? string.Empty;

        // Quoted and block scalars are always text.
        if (scalar.Style != ScalarStyle.Plain)
        {
            return JsonValue.Create(value);
        }

        switch (value)
        {
            case "" or "~" or "null" or "Null" or "NULL":
                return null;
            case "true" or "True" or "TRUE":
                return JsonValue.Create(true);
            case "false" or "False" or "FALSE":
                return JsonValue.Create(false);
        }

        if (IntegerRegex().IsMatch(value) && long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer))
        {
            return JsonValue.Create(integer);
        }

        if (FloatRegex().IsMatch(value)
            && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && double.IsFinite(number))
        {
            return JsonValue.Create(number);
        }

        return JsonValue.Create(value);
    }

    [GeneratedRegex("^[-+]?[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex IntegerRegex();

    [GeneratedRegex(@"^[-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex FloatRegex();
}
