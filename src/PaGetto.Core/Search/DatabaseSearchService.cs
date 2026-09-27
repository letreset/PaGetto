using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Entities;
using PaGetto.Core.Indexing;
using PaGetto.Core.Metadata;
using PaGetto.Protocol.Models;
using Microsoft.EntityFrameworkCore;
using NuGet.Versioning;

namespace PaGetto.Core.Search;

public class DatabaseSearchService : ISearchService
{
    private readonly IContext _context;
    private readonly IFrameworkCompatibilityService _frameworks;
    private readonly ISearchResponseBuilder _searchBuilder;

    public DatabaseSearchService(IContext context, IFrameworkCompatibilityService frameworks, ISearchResponseBuilder searchBuilder)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(frameworks);
        ArgumentNullException.ThrowIfNull(searchBuilder);

        _context = context;
        _frameworks = frameworks;
        _searchBuilder = searchBuilder;
    }

    public async Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        var frameworks = GetCompatibleFrameworksOrNull(request.Framework);

        var query = string.IsNullOrEmpty(request.Query) ? null : request.Query.ToLowerInvariant();

        var search = _context.Packages.Where(p => p.FeedId == request.FeedId);
        search = await ApplyFullTextQueryAsync(search, request.FeedId, query, cancellationToken);
        search = ApplySearchFilters(
            search,
            request.IncludePrerelease,
            request.IncludeSemVer2,
            request.PackageType,
            frameworks,
            request.IncludeUnlisted);

        if (!string.IsNullOrEmpty(request.Tag))
        {
            var taggedPackageIds = await GetPackageIdsWithTagAsync(request.FeedId, request.Tag, request.IncludeUnlisted, cancellationToken);
            search = search.Where(p => taggedPackageIds.Contains(p.Id));
        }

        // Count over distinct IDs, not rows: rows are per-version.
        var totalHits = await search
            .Select(p => p.Id)
            .Distinct()
            .CountAsync(cancellationToken);

        // Id matches come first (exact, then prefix, then contains), then packages that only match
        // in their title, description, tags or authors. GROUP BY rather than DISTINCT, so the
        // ORDER BY expression is valid on every database.
        var distinctIds = search.GroupBy(p => p.Id).Select(g => g.Key);
        var orderedIds = query == null
            ? distinctIds.OrderBy(id => id)
            : distinctIds
#pragma warning disable CA1862 // Not for EF queries: StringComparison overloads aren't translated to SQL.
                .OrderBy(id => id.ToLower() == query ? 0 : id.ToLower().StartsWith(query) ? 1 : id.ToLower().Contains(query) ? 2 : 3)
#pragma warning restore CA1862
                .ThenBy(id => id);
        var packageIds = orderedIds
            .Skip(request.Skip)
            .Take(request.Take);

        // This query MUST fetch all versions for each package that matches the search,
        // otherwise the results for a package's latest version may be incorrect.
        // If possible, we'll find all these packages in a single query by matching
        // the package IDs in a subquery. Otherwise, run two queries:
        //   1. Find the package IDs that match the search
        //   2. Find all package versions for these package IDs
        if (_context.SupportsLimitInSubqueries)
        {
            search = _context.Packages.Where(p => p.FeedId == request.FeedId && packageIds.Contains(p.Id));
        }
        else
        {
            var packageIdResults = await packageIds.ToListAsync(cancellationToken);

            search = _context.Packages.Where(p => p.FeedId == request.FeedId && packageIdResults.Contains(p.Id));
        }

        search = ApplySearchFilters(
            search,
            request.IncludePrerelease,
            request.IncludeSemVer2,
            request.PackageType,
            frameworks,
            request.IncludeUnlisted);

        var results = await search.ToListAsync(cancellationToken);
        var groupedResults = results
            .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => new PackageRegistration(group.Key, group.ToList()))
            .OrderBy(r => Rank(r.PackageId, query))
            .ThenBy(r => r.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var response = _searchBuilder.BuildSearch(groupedResults, request.IncludeUnlisted);
        response.TotalHits = totalHits;

        if (request.IncludeFacets)
        {
            response.Facets = await ComputeFacetsAsync(request, cancellationToken);
        }

        return response;
    }

    public async Task<AutocompleteResponse> AutocompleteAsync(AutocompleteRequest request, CancellationToken cancellationToken)
    {
        var search = _context.Packages.Where(p => p.FeedId == request.FeedId);

        search = ApplySearchQuery(search, request.Query);
        search = ApplySearchFilters(
            search,
            request.IncludePrerelease,
            request.IncludeSemVer2,
            request.PackageType,
            frameworks: null);

        var packageIds = await search
            .OrderByDescending(p => p.Downloads)
            .Select(p => p.Id)
            .Distinct()
            .Skip(request.Skip)
            .Take(request.Take)
            .ToListAsync(cancellationToken);

        return _searchBuilder.BuildAutocomplete(packageIds);
    }

    public async Task<AutocompleteResponse> ListPackageVersionsAsync(VersionsRequest request, CancellationToken cancellationToken)
    {
        var packageId = request.PackageId.ToLower();
        var search = _context
            .Packages
            .Where(p => p.FeedId == request.FeedId && p.Id.ToLower().Equals(packageId));

        search = ApplySearchFilters(
            search,
            request.IncludePrerelease,
            request.IncludeSemVer2,
            packageType: null,
            frameworks: null);

        var packageVersions = await search
            .Select(p => p.NormalizedVersionString)
            .ToListAsync(cancellationToken);

        return _searchBuilder.BuildAutocomplete(packageVersions);
    }

    public async Task<DependentsResponse> FindDependentsAsync(Guid feedId, string packageId, CancellationToken cancellationToken)
    {
        var dependentVersions = _context
            .Packages
            .Where(p => p.FeedId == feedId && p.Listed)
            .Where(p => p.Dependencies.Any(d => d.Id == packageId));

        // One row per package, not per version: downloads summed over its listed versions.
        var top = await dependentVersions
            .GroupBy(p => p.Id)
            .Select(g => new { Id = g.Key, TotalDownloads = g.Sum(p => p.Downloads) })
            .OrderByDescending(g => g.TotalDownloads)
            .ThenBy(g => g.Id)
            .Take(20)
            .ToListAsync(cancellationToken);

        var ids = top.Select(t => t.Id).ToList();
        var versions = await dependentVersions
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.NormalizedVersionString, p.Description })
            .ToListAsync(cancellationToken);

        // The description comes from the newest listed version that depends on the package.
        var descriptions = versions
            .GroupBy(v => v.Id)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(v => NuGetVersion.Parse(v.NormalizedVersionString)).First().Description);

        var dependents = top
            .Select(t => new PackageDependent
            {
                Id = t.Id,
                Description = descriptions.GetValueOrDefault(t.Id),
                TotalDownloads = t.TotalDownloads
            })
            .ToList();

        return _searchBuilder.BuildDependents(dependents);
    }

    /// <summary>
    /// The in-memory counterpart of the ORDER BY in <see cref="SearchAsync"/>.
    /// </summary>
    private static int Rank(string packageId, string query)
    {
        if (query == null) return 0;

        var id = packageId.ToLowerInvariant();
        if (id == query) return 0;
        if (id.StartsWith(query, StringComparison.Ordinal)) return 1;
        return id.Contains(query, StringComparison.Ordinal) ? 2 : 3;
    }

    /// <summary>
    /// Matches the (lowercased) query against the id, title, description, tags and authors.
    /// Tags and authors are stored as JSON strings (value converter), so they can't be searched in
    /// SQL; they are matched in memory, like the tag filter. A tag matches when it starts with the
    /// query, so <c>orm</c> finds the <c>orm</c> tag but not <c>platform</c>.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1862:Use the 'StringComparison' method overloads to perform case-insensitive string comparisons", Justification = "Not for EF queries")]
    private async Task<IQueryable<Package>> ApplyFullTextQueryAsync(
        IQueryable<Package> query,
        Guid feedId,
        string search,
        CancellationToken cancellationToken)
    {
        if (search == null)
        {
            return query;
        }

        var rows = await _context.Packages
            .Where(p => p.FeedId == feedId)
            .Select(p => new { p.Id, p.Tags, p.Authors })
            .ToListAsync(cancellationToken);

        var tagOrAuthorMatches = rows
            .Where(r =>
                (r.Tags != null && r.Tags.Any(t => t != null && t.StartsWith(search, StringComparison.OrdinalIgnoreCase))) ||
                (r.Authors != null && r.Authors.Any(a => a != null && a.Contains(search, StringComparison.OrdinalIgnoreCase))))
            .Select(r => r.Id)
            .Distinct()
            .ToList();

        return query.Where(p =>
            p.Id.ToLower().Contains(search) ||
            (p.Title != null && p.Title.ToLower().Contains(search)) ||
            (p.Description != null && p.Description.ToLower().Contains(search)) ||
            tagOrAuthorMatches.Contains(p.Id));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1862:Use the 'StringComparison' method overloads to perform case-insensitive string comparisons", Justification = "Not for EF queries")]
    private static IQueryable<Package> ApplySearchQuery(IQueryable<Package> query, string search)
    {
        if (string.IsNullOrEmpty(search))
        {
            return query;
        }

        search = search.ToLowerInvariant();

        return query.Where(p => p.Id.ToLower().Contains(search));
    }

    private static IQueryable<Package> ApplySearchFilters(
        IQueryable<Package> query,
        bool includePrerelease,
        bool includeSemVer2,
        string packageType,
        IReadOnlyList<string> frameworks,
        bool includeUnlisted = false)
    {
        if (!includePrerelease)
        {
            query = query.Where(p => !p.IsPrerelease);
        }

        if (!includeSemVer2)
        {
            query = query.Where(p => p.SemVerLevel != SemVerLevel.SemVer2);
        }

        if (!string.IsNullOrEmpty(packageType))
        {
            query = query.Where(p => p.PackageTypes.Any(t => t.Name == packageType));
        }

        if (frameworks != null)
        {
            query = query.Where(p => p.TargetFrameworks.Any(f => frameworks.Contains(f.Moniker)));
        }

        if (!includeUnlisted)
        {
            query = query.Where(p => p.Listed);
        }

        return query;
    }

    private IReadOnlyList<string> GetCompatibleFrameworksOrNull(string framework)
    {
        if (framework == null) return null;

        return _frameworks.FindAllCompatibleFrameworks(framework);
    }

    /// <summary>
    /// Finds the IDs of the packages in a feed that carry a given tag.
    /// Tags are stored as a JSON string (value converter), so they can't be filtered in SQL;
    /// the (id, tags) pairs are materialized and matched in memory instead.
    /// </summary>
    private async Task<List<string>> GetPackageIdsWithTagAsync(Guid feedId, string tag, bool includeUnlisted, CancellationToken cancellationToken)
    {
        var rows = await _context.Packages
            .Where(p => p.FeedId == feedId && (includeUnlisted || p.Listed))
            .Select(p => new { p.Id, p.Tags })
            .ToListAsync(cancellationToken);

        return rows
            .Where(r => r.Tags != null && r.Tags.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)))
            .Select(r => r.Id)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Computes the distinct package types, frameworks and tags present in a feed so the UI can
    /// populate its filter dropdowns with only the values that actually occur. Honors the prerelease
    /// and SemVer2 toggles, but is independent of the query and the package type/framework/tag filters.
    /// </summary>
    private async Task<SearchFacets> ComputeFacetsAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        var query = _context.Packages.Where(p => p.FeedId == request.FeedId && p.Listed);

        if (!request.IncludePrerelease)
        {
            query = query.Where(p => !p.IsPrerelease);
        }

        if (!request.IncludeSemVer2)
        {
            query = query.Where(p => p.SemVerLevel != SemVerLevel.SemVer2);
        }

        var packageTypes = await query
            .SelectMany(p => p.PackageTypes.Select(t => t.Name))
            .Where(name => !string.IsNullOrEmpty(name))
            .Distinct()
            .ToListAsync(cancellationToken);

        var frameworks = await query
            .SelectMany(p => p.TargetFrameworks.Select(f => f.Moniker))
            .Where(moniker => !string.IsNullOrEmpty(moniker))
            .Distinct()
            .ToListAsync(cancellationToken);

        // Tags are stored as a JSON string (value converter), so aggregate them in memory.
        var tagArrays = await query
            .Select(p => p.Tags)
            .ToListAsync(cancellationToken);

        var tags = tagArrays
            .Where(t => t != null)
            .SelectMany(t => t)
            .Where(t => !string.IsNullOrWhiteSpace(t));

        // "any" is the dropdowns' "no filter" sentinel; it's also the moniker stored for
        // framework-agnostic packages. Drop it so it doesn't show up as a bogus filter value.
        return new SearchFacets
        {
            PackageTypes = SortFacet(packageTypes),
            Frameworks = SortFacet(frameworks),
            Tags = SortFacet(tags),
        };
    }

    private static List<string> SortFacet(IEnumerable<string> values)
    {
        return values
            .Where(v => !string.Equals(v, "any", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
