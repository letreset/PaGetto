using System;
using System.IO;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Artifacts;
using PaGetto.Core.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace PaGetto.Web.Extensions;

public static class HttpRequestExtensions
{
    public const string ApiKeyHeader = "X-NuGet-ApiKey";

    /// <summary>
    /// The most a multipart form may add to the file it carries: its boundaries, part headers and
    /// small fields.
    /// </summary>
    public const long MaxFormOverheadBytes = 64 * 1024;

    /// <summary>
    /// Reads an uploaded file, the first file of a multipart form or else the request body, into a
    /// temporary file.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="maxBytes">The largest file accepted, the feed's package size limit.</param>
    /// <param name="cancellationToken">A token to cancel the task.</param>
    /// <returns>The file, or null if the form has no file.</returns>
    /// <exception cref="ArtifactFileTooLargeException">
    /// The file is larger than <paramref name="maxBytes"/>. A request that declares a larger length
    /// is refused before any of its body is read.
    /// </exception>
    public static async Task<Stream> GetUploadStreamOrNullAsync(this HttpRequest request, long maxBytes, CancellationToken cancellationToken)
    {
        // The server-wide request limit applies before the feed is known; a feed can only lower it.
        var maxRequestBytes = maxBytes + MaxFormOverheadBytes;
        if (request.ContentLength > maxRequestBytes)
        {
            throw new ArtifactFileTooLargeException(maxBytes);
        }

        var sizeLimit = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeLimit is { IsReadOnly: false })
        {
            sizeLimit.MaxRequestBodySize = maxRequestBytes;
        }

        Stream rawUploadStream = null;
        try
        {
            if (request.HasFormContentType)
            {
                var form = await request.ReadFormAsync(cancellationToken);
                rawUploadStream = form.Files.Count > 0 ? form.Files[0].OpenReadStream() : request.Body;
            }
            else
            {
                rawUploadStream = request.Body;
            }

            // Convert the upload stream into a temporary file stream to
            // minimize memory usage.
            return await rawUploadStream.AsTemporaryFileStreamAsync(maxBytes, cancellationToken);
        }
        catch (Exception e) when (e is InvalidDataException
            || (e is BadHttpRequestException bad && bad.StatusCode == StatusCodes.Status413PayloadTooLarge))
        {
            // InvalidDataException is a form part over the server's limit.
            throw new ArtifactFileTooLargeException(maxBytes);
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
