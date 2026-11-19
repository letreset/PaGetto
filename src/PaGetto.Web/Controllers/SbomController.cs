using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PaGetto.Core.Authentication;
using PaGetto.Core.Feeds;
using PaGetto.Core.Sbom;

namespace PaGetto.Web.Controllers;

/// <summary>
/// The software bill of materials of a stored version of any feed type, in CycloneDX JSON:
/// <c>GET {feed}/api/v1/sbom/{id}/{version}</c>. A name with a slash (a scoped npm package, a Go
/// module, a Composer package) has it encoded as <c>%2F</c>.
/// </summary>
[Authorize(AuthenticationSchemes = AuthenticationConstants.NugetBasicAuthenticationScheme, Policy = AuthenticationConstants.NugetUserPolicy)]
public partial class SbomController : Controller
{
    private readonly IFeedContext _feedContext;
    private readonly SbomService _sbom;

    public SbomController(IFeedContext feedContext, SbomService sbom)
    {
        _feedContext = feedContext ?? throw new ArgumentNullException(nameof(feedContext));
        _sbom = sbom ?? throw new ArgumentNullException(nameof(sbom));
    }

    [HttpGet]
    public async Task<IActionResult> CycloneDx(string id, string version, CancellationToken cancellationToken)
    {
        // ASP.NET Core leaves an encoded slash encoded in route values.
        var name = id?.Replace("%2F", "/", StringComparison.OrdinalIgnoreCase);

        var document = await _sbom.GetCycloneDxOrNullAsync(_feedContext.CurrentFeed, name, version, cancellationToken);
        if (document == null)
        {
            return NotFound();
        }

        var fileName = $"{UnsafeFileNameCharacters().Replace($"{name}-{version}", "_").Trim('_')}.cdx.json";

        return File(document, $"{CycloneDxWriter.MediaType}; version={CycloneDxWriter.SpecVersion}", fileName);
    }

    [GeneratedRegex("[^A-Za-z0-9._-]+", RegexOptions.CultureInvariant)]
    private static partial Regex UnsafeFileNameCharacters();
}
