using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PaGetto.Core.Cran;

/// <summary>
/// The parts of an R package version's manifest (see <see cref="CranManifest"/>) that are shown on
/// a package's page.
/// </summary>
public sealed partial class CranManifestSummary
{
    /// <summary>
    /// The package name as published, or null.
    /// </summary>
    public string Name { get; private init; }

    /// <summary>
    /// The R versions the package supports, from its dependency on R, e.g. <c>&gt;= 4.1.0</c>, or null.
    /// </summary>
    public string RVersion { get; private init; }

    /// <summary>
    /// The packages of <c>Depends</c>, <c>Imports</c> and <c>LinkingTo</c>, as name and version
    /// requirement (empty when there is none), without R itself.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, string>> Dependencies { get; private init; }

    /// <summary>
    /// The links, by the names the package page knows (<c>homepage</c> from <c>URL</c>,
    /// <c>issue_tracker</c> from <c>BugReports</c>). Only http and https URLs are kept, so the links
    /// are safe to render.
    /// </summary>
    public IReadOnlyDictionary<string, string> Links { get; private init; }

    public string License { get; private init; }

    public string Author { get; private init; }

    public string Maintainer { get; private init; }

    /// <summary>
    /// Whether the package has code to compile (<c>NeedsCompilation: yes</c>).
    /// </summary>
    public bool NeedsCompilation { get; private init; }

    /// <summary>
    /// Reads a manifest. Missing fields are left empty, and anything that is not a manifest gives an empty summary.
    /// </summary>
    public static CranManifestSummary Parse(string manifestJson)
    {
        var fields = CranManifest.Parse(manifestJson);

        string rVersion = null;
        var dependencies = new List<KeyValuePair<string, string>>();
        foreach (var field in new[] { "Depends", "Imports", "LinkingTo" })
        {
            foreach (var (name, requirement) in ParseDependencies(CranDcf.GetValue(fields, field)))
            {
                if (name == "R")
                {
                    rVersion ??= requirement.Length > 0 ? requirement : null;
                }
                else if (!dependencies.Exists(d => d.Key == name))
                {
                    dependencies.Add(KeyValuePair.Create(name, requirement));
                }
            }
        }

        var links = new Dictionary<string, string>();
        AddLink(links, "homepage", CranDcf.GetValue(fields, "URL"));
        AddLink(links, "issue_tracker", CranDcf.GetValue(fields, "BugReports"));

        return new CranManifestSummary
        {
            Name = CranDcf.GetValue(fields, CranManifest.PackageField),
            RVersion = rVersion,
            Dependencies = dependencies,
            Links = links,
            License = OneLine(CranDcf.GetValue(fields, "License")),
            Author = OneLine(CranDcf.GetValue(fields, "Author")),
            Maintainer = OneLine(CranDcf.GetValue(fields, "Maintainer")),
            NeedsCompilation = string.Equals(CranDcf.GetValue(fields, "NeedsCompilation"), "yes", StringComparison.OrdinalIgnoreCase),
        };
    }

    /// <summary>
    /// Reads a dependency field: a comma-separated list of package names, each with an optional
    /// version requirement in parentheses, e.g. <c>R (&gt;= 4.1), rlang (&gt;= 1.0.0), cli</c>.
    /// </summary>
    private static IEnumerable<KeyValuePair<string, string>> ParseDependencies(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        foreach (var item in value.Split(','))
        {
            var match = DependencyRegex().Match(item.Trim());
            if (match.Success)
            {
                yield return KeyValuePair.Create(match.Groups[1].Value, OneLine(match.Groups[2].Value) ?? "");
            }
        }
    }

    /// <summary>
    /// Adds the first http or https URL of a field, which may list several separated by commas or white space.
    /// </summary>
    private static void AddLink(Dictionary<string, string> links, string name, string value)
    {
        foreach (var candidate in (value ?? "").Split([',', ' ', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                links[name] = uri.AbsoluteUri;
                return;
            }
        }
    }

    private static string OneLine(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : WhiteSpaceRegex().Replace(value, " ").Trim();
    }

    [GeneratedRegex(@"^([A-Za-z][A-Za-z0-9.]*)\s*(?:\((.*)\))?$", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex DependencyRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhiteSpaceRegex();
}
