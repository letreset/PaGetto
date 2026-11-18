using System;
using System.IO;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Extensions;
using Microsoft.AspNetCore.Http;

namespace PaGetto.Web.Extensions;

public static class HttpRequestExtensions
{
    public const string ApiKeyHeader = "X-NuGet-ApiKey";

    public static async Task<Stream> GetUploadStreamOrNullAsync(this HttpRequest request, CancellationToken cancellationToken)
    {
        // Try to get the nupkg from the multipart/form-data. If that's empty,
        // fallback to the request's body.
        Stream rawUploadStream = null;
        try
        {
            if (request.HasFormContentType && request.Form.Files.Count > 0)
            {
                rawUploadStream = request.Form.Files[0].OpenReadStream();
            }
            else
            {
                rawUploadStream = request.Body;
            }

            // Convert the upload stream into a temporary file stream to
            // minimize memory usage.
            return await rawUploadStream?.AsTemporaryFileStreamAsync(cancellationToken);
        }
        finally
        {
            rawUploadStream?.Dispose();
        }
    }

    public static string GetApiKey(this HttpRequest request)
    {
        return request.Headers[ApiKeyHeader];
    }

    /// <summary>
    /// The API key a package client sent, which the <c>Legacy</c> authentication mode checks: the
    /// NuGet API key header, a Bearer token, or the password of Basic authentication.
    /// </summary>
    public static string GetApiKeyOrPassword(this HttpRequest request)
    {
        var apiKey = request.GetApiKey();
        if (!string.IsNullOrEmpty(apiKey))
        {
            return apiKey;
        }

        if (!AuthenticationHeaderValue.TryParse(request.Headers.Authorization, out var header) || header.Parameter == null)
        {
            return null;
        }

        if (string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            return header.Parameter;
        }

        try
        {
            var credentials = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter)).Split(':', 2);
            return credentials.Length == 2 ? credentials[1] : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
