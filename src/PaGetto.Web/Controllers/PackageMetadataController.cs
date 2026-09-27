using System;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Authentication;
using PaGetto.Core.Feeds;
using PaGetto.Core.Metadata;
using PaGetto.Protocol.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NuGet.Versioning;

namespace PaGetto.Web.Controllers;

/// <summary>
/// The Package Metadata resource, used to fetch packages' information.
/// See: https://docs.microsoft.com/en-us/nuget/api/registration-base-url-resource
/// </summary>

[Authorize(AuthenticationSchemes = AuthenticationConstants.NugetBasicAuthenticationScheme, Policy = AuthenticationConstants.NugetUserPolicy)]
public class PackageMetadataController : Controller
{
    private readonly IPackageMetadataService _metadata;
    private readonly IFeedContext _feedContext;

    public PackageMetadataController(IPackageMetadataService metadata, IFeedContext feedContext)
    {
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _feedContext = feedContext ?? throw new ArgumentNullException(nameof(feedContext));
    }

    // GET v3/registration/{id}.json
    [HttpGet]
    [ContentETag]
    public async Task<ActionResult<PaGettoRegistrationIndexResponse>> RegistrationIndexAsync(string id, CancellationToken cancellationToken)
    {
        var index = await _metadata.GetRegistrationIndexOrNullAsync(_feedContext.CurrentFeed.Id, _feedContext.CurrentFeed.Slug, id, cancellationToken);
        if (index == null)
        {
            return NotFound();
        }

        return index;
    }

    // GET v3/registration/{id}/page/{lower}/{upper}.json
    [HttpGet]
    [ContentETag]
    public async Task<ActionResult<PaGettoRegistrationPageResponse>> RegistrationPageAsync(string id, string lower, string upper, CancellationToken cancellationToken)
    {
        if (!NuGetVersion.TryParse(lower, out var lowerVersion) ||
            !NuGetVersion.TryParse(upper, out var upperVersion))
        {
            return NotFound();
        }

        var page = await _metadata.GetRegistrationPageOrNullAsync(_feedContext.CurrentFeed.Id, _feedContext.CurrentFeed.Slug, id, lowerVersion, upperVersion, cancellationToken);
        if (page == null)
        {
            return NotFound();
        }

        return page;
    }

    // GET v3/registration/{id}/{version}.json
    [HttpGet]
    [ContentETag]
    public async Task<ActionResult<RegistrationLeafResponse>> RegistrationLeafAsync(string id, string version, CancellationToken cancellationToken)
    {
        if (!NuGetVersion.TryParse(version, out var nugetVersion))
        {
            return NotFound();
        }

        var leaf = await _metadata.GetRegistrationLeafOrNullAsync(_feedContext.CurrentFeed.Id, _feedContext.CurrentFeed.Slug, id, nugetVersion, cancellationToken);
        if (leaf == null)
        {
            return NotFound();
        }

        return leaf;
    }
}
