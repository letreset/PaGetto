using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// <c>npm audit</c>'s bulk advisory request, answered by the feed's mirrors.
/// </summary>
public class NpmAuditIntegrationTests : IDisposable
{
    private const string Feed = "proxy";
    private const string Advisories = """{"lodash":[{"id":1,"severity":"high","vulnerable_versions":"<4.17.21"}]}""";

    private readonly AdvisoryUpstream _upstream = new();
    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public NpmAuditIntegrationTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output, _upstream);
        _client = _app.CreateClient();
    }

    [Fact]
    public async Task AsksTheMirrorWithoutTheFeedsOwnPackages()
    {
        await _app.CreateNpmFeedAsync(Feed, mirrorSource: "https://registry.test/");
        using (await _client.PublishAsync(Feed, "internal-lib", "1.0.0"))
        {
        }

        using var response = await AuditAsync("""{"internal-lib":["1.0.0"],"lodash":["4.17.0"]}""", gzip: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Advisories, await response.Content.ReadAsStringAsync());
        Assert.Equal("/-/npm/v1/security/advisories/bulk", _upstream.Path);

        // A private package name never reaches the mirror.
        var asked = JsonNode.Parse(_upstream.Body)!.AsObject();
        Assert.True(asked.ContainsKey("lodash"));
        Assert.False(asked.ContainsKey("internal-lib"));
    }

    [Fact]
    public async Task AFeedWithoutMirrorsKnowsNoAdvisories()
    {
        await _app.CreateNpmFeedAsync(Feed);

        using var response = await AuditAsync("""{"lodash":["4.17.0"]}""", gzip: false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{}", await response.Content.ReadAsStringAsync());
        Assert.Null(_upstream.Path);
    }

    [Fact]
    public async Task AFailingMirrorIsABadGateway()
    {
        await _app.CreateNpmFeedAsync(Feed, mirrorSource: "https://registry.test/");
        _upstream.Status = HttpStatusCode.InternalServerError;

        using var response = await AuditAsync("""{"lodash":["4.17.0"]}""", gzip: false);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task ABodyThatIsNotAnObjectIsABadRequest()
    {
        await _app.CreateNpmFeedAsync(Feed);

        using var response = await AuditAsync("[1, 2]", gzip: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<HttpResponseMessage> AuditAsync(string json, bool gzip)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        if (gzip)
        {
            using var buffer = new MemoryStream();
            using (var compressor = new GZipStream(buffer, CompressionMode.Compress, leaveOpen: true))
            {
                compressor.Write(bytes);
            }

            bytes = buffer.ToArray();
        }

        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        if (gzip)
        {
            content.Headers.ContentEncoding.Add("gzip");
        }

        return await _client.PostAsync($"feeds/{Feed}/npm/-/npm/v1/security/advisories/bulk", content);
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
        _upstream.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// A registry that answers the advisory request with fixed advisories and records it.
    /// </summary>
    private sealed class AdvisoryUpstream : HttpMessageHandler
    {
        public string Path { get; private set; }
        public string Body { get; private set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Post)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            Path = request.RequestUri!.AbsolutePath;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(Status) { Content = new StringContent(Advisories, Encoding.UTF8, "application/json") };
        }
    }
}
