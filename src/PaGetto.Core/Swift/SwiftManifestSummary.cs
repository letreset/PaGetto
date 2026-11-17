using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace PaGetto.Core.Swift;

/// <summary>
/// The parts of a Swift package release's manifest that are shown on a package's page.
/// </summary>
public sealed class SwiftManifestSummary
{
    /// <summary>
    /// The identifier as published, e.g. <c>mona.LinkedList</c>, or null.
    /// </summary>
    public string Id { get; private init; }

    /// <summary>
    /// The tools version <c>Package.swift</c> declares, e.g. <c>5.9</c>, or null.
    /// </summary>
    public string ToolsVersion { get; private init; }

    /// <summary>
    /// The author's name, or null.
    /// </summary>
    public string Author { get; private init; }

    /// <summary>
    /// The links, by the names the package page knows (<c>repository</c>, <c>license</c>). Only http
    /// and https URLs are kept, so the links are safe to render.
    /// </summary>
    public IReadOnlyDictionary<string, string> Links { get; private init; }

    /// <summary>
    /// Whether the source archive is signed.
    /// </summary>
    public bool IsSigned { get; private init; }

    /// <summary>
    /// Reads a stored manifest. Fields that are missing or have an unexpected type are skipped.
    /// </summary>
    public static SwiftManifestSummary Parse(string manifestJson)
    {
        var manifest = SwiftManifest.Parse(manifestJson);

        var links = new Dictionary<string, string>();
        AddLink(links, "repository", SwiftReleaseMetadata.GetRepositoryUrls(manifest.Metadata).FirstOrDefault(IsWebUrl));
        AddLink(links, "license", GetString(manifest.Metadata, "licenseURL"));

        return new SwiftManifestSummary
        {
            Id = manifest.Id,
            ToolsVersion = SwiftManifest.GetToolsVersion(manifest.Manifests.GetValueOrDefault(SwiftManifest.ManifestFileName)),
            Author = GetString(manifest.Metadata["author"] as JsonObject, "name"),
            Links = links,
            IsSigned = manifest.Signature != null,
        };
    }

    private static void AddLink(Dictionary<string, string> links, string name, string url)
    {
        if (IsWebUrl(url))
        {
            links[name] = new Uri(url).AbsoluteUri;
        }
    }

    private static bool IsWebUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static string GetString(JsonObject item, string property)
    {
        return item?[property] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text
            : null;
    }
}
