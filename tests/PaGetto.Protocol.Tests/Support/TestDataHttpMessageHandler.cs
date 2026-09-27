using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PaGetto.Protocol.Tests.Support;

/// <remarks>
/// </remarks>
public class TestDataHttpMessageHandler : HttpMessageHandler
{
    private static readonly Dictionary<string, Func<string>> _urlToGetContent = new Dictionary<string, Func<string>>
    {
        { TestData.ServiceIndexUrl, () => TestData.ServiceIndex },
        { TestData.CatalogIndexUrl, () => TestData.CatalogIndex },
        { TestData.CatalogPageUrl, () => TestData.CatalogPage },
        { TestData.PackageDeleteCatalogLeafUrl, () => TestData.PackageDeleteCatalogLeaf },
        { TestData.PackageDetailsCatalogLeafUrl, () => TestData.PackageDetailsCatalogLeaf },
        //{ TestData.CatalogLeafInvalidDependencyVersionRangeUrl, () => TestData.CatalogLeafInvalidDependencyVersionRange },
        { TestData.RegistrationIndexInlinedItemsUrl, () => TestData.RegistrationIndexInlinedItems },
        { TestData.RegistrationIndexLikeGithubPackagesUrl, () => TestData.RegistrationIndexLikeGithubPackages },
        { TestData.RegistrationIndexPagedItemsUrl, () => TestData.RegistrationIndexPagedItems },
        { TestData.RegistrationLeafUnlistedUrl, () => TestData.RegistrationLeafUnlisted },
        { TestData.RegistrationLeafListedUrl, () => TestData.RegistrationLeafListed },
        { TestData.RegistrationPageUrl, () => TestData.RegistrationPage },
        { TestData.PackageContentVersionListUrl, () => TestData.PackageContentVersionList },
        { TestData.DefaultSearchUrl, () => TestData.DefaultSearch },
        { TestData.DefaultAutocompleteUrl, () => TestData.DefaultAutocomplete },
    };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(Send(request));
    }

    private HttpResponseMessage Send(HttpRequestMessage request)
    {
        Func<string> getContent;
        if (request.Method != HttpMethod.Get
            || !_urlToGetContent.TryGetValue(request.RequestUri.AbsoluteUri, out getContent))
        {
            return new HttpResponseMessage
            {
                RequestMessage = request,
                StatusCode = HttpStatusCode.NotFound,
                Content = new StringContent(string.Empty),
            };
        }

        return new HttpResponseMessage
        {
            RequestMessage = request,
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(
                getContent(),
                encoding: null,
                mediaType: "application/json"),
        };
    }
}
