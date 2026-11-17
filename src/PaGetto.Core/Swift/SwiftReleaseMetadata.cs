using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PaGetto.Core.Swift;

/// <summary>
/// The metadata a release is published with (the <c>metadata</c> part of a publish), which the
/// registry API returns with the release. Swift Package Manager decodes its fields with fixed
/// types, so a field of another type would make every client fail on the release; those are
/// refused. Other fields are kept as they are.
/// See: https://github.com/swiftlang/swift-package-manager/blob/main/Documentation/PackageRegistry/Registry.md#appendix-b---package-release-metadata-json-schema
/// </summary>
public static class SwiftReleaseMetadata
{
    private const int MaxRepositoryUrls = 100;

    private static readonly string[] _stringFields = ["description", "licenseURL", "readmeURL"];
    private static readonly string[] _authorStringFields = ["email", "description", "url"];

    /// <summary>
    /// Reads and checks the metadata of a publish.
    /// </summary>
    /// <param name="json">The metadata, or null or empty when the publisher sent none.</param>
    /// <returns>The metadata, an empty object when there is none.</returns>
    /// <exception cref="InvalidSwiftPackageException">The metadata is not valid.</exception>
    public static JsonObject Read(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new JsonObject();
        }

        JsonObject metadata;
        try
        {
            metadata = JsonNode.Parse(json) as JsonObject
                ?? throw new InvalidSwiftPackageException("The metadata is not a JSON object.");
        }
        catch (JsonException e)
        {
            throw new InvalidSwiftPackageException($"The metadata is not valid JSON: {e.Message}", e);
        }

        foreach (var field in _stringFields)
        {
            CheckString(metadata, field, field);
        }

        if (metadata["repositoryURLs"] is { } urls
            && (urls is not JsonArray items || items.Count > MaxRepositoryUrls || items.Any(i => GetText(i) == null)))
        {
            throw new InvalidSwiftPackageException($"The metadata field repositoryURLs must be an array of at most {MaxRepositoryUrls} strings.");
        }

        if (metadata["author"] is { } author)
        {
            CheckParty(author, "author");

            if (author["organization"] is { } organization)
            {
                CheckParty(organization, "author.organization");
            }
        }

        if (metadata["originalPublicationTime"] is { } time)
        {
            if (!DateTimeOffset.TryParse(GetText(time), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                throw new InvalidSwiftPackageException("The metadata field originalPublicationTime must be an ISO 8601 date and time.");
            }

            // Swift Package Manager reads times without fractions of a second only.
            metadata["originalPublicationTime"] = SwiftManifest.FormatTime(parsed.UtcDateTime);
        }

        return metadata;
    }

    /// <summary>
    /// The repository URLs of the metadata, which <c>GET /identifiers?url=</c> looks packages up by.
    /// </summary>
    public static IReadOnlyList<string> GetRepositoryUrls(JsonObject metadata)
    {
        return (metadata?["repositoryURLs"] as JsonArray)?.Select(GetText).Where(url => url != null).ToList() ?? [];
    }

    /// <summary>
    /// A repository URL in a form that the ways of writing the same repository share:
    /// <c>https://github.com/mona/LinkedList.git</c>, <c>git@github.com:mona/LinkedList.git</c> and
    /// <c>ssh://git@github.com/mona/LinkedList</c> are all <c>github.com/mona/linkedlist</c>.
    /// </summary>
    public static string NormalizeRepositoryUrl(string url)
    {
        var text = url?.Trim().ToLowerInvariant() ?? "";

        var scheme = text.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            text = text[(scheme + 3)..];
        }

        var at = text.IndexOf('@');
        var slash = text.IndexOf('/');
        if (at >= 0 && (slash < 0 || at < slash))
        {
            text = text[(at + 1)..];
        }

        // The scp-like form git@host:path.
        var colon = text.IndexOf(':');
        slash = text.IndexOf('/');
        if (colon >= 0 && (slash < 0 || colon < slash) && !text[(colon + 1)..].TakeWhile(c => c != '/').All(char.IsAsciiDigit))
        {
            text = text[..colon] + "/" + text[(colon + 1)..];
        }

        text = text.TrimEnd('/');
        if (text.EndsWith(".git", StringComparison.Ordinal))
        {
            text = text[..^4];
        }

        return text;
    }

    private static void CheckParty(JsonNode node, string path)
    {
        if (node is not JsonObject party || GetText(party["name"]) == null)
        {
            throw new InvalidSwiftPackageException($"The metadata field {path} must be an object with a name.");
        }

        foreach (var field in _authorStringFields)
        {
            CheckString(party, field, $"{path}.{field}");
        }
    }

    private static void CheckString(JsonObject item, string field, string path)
    {
        if (item[field] is { } value && GetText(value) == null)
        {
            throw new InvalidSwiftPackageException($"The metadata field {path} must be a string.");
        }
    }

    private static string GetText(JsonNode node)
    {
        return node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }
}
