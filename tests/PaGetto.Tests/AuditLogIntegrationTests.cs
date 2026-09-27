using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Audit;
using PaGetto.Core.Extensions;
using PaGetto.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// NuGet API actions are stored for the admin audit page: successful ones only.
/// </summary>
public class AuditLogIntegrationTests : IDisposable
{
    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public AuditLogIntegrationTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output);
        _client = _app.CreateClient();
    }

    [Fact]
    public async Task StoresASuccessfulPushButNotARejectedOne()
    {
        Assert.Equal(HttpStatusCode.Created, await PushAsync());
        Assert.Equal(HttpStatusCode.Conflict, await PushAsync());

        using var scope = _app.Services.CreateScope();
        var auditEvents = scope.ServiceProvider.GetRequiredService<IAuditEventService>();
        var (events, total) = await auditEvents.SearchAsync(new AuditEventFilter(), 0, 10, CancellationToken.None);

        Assert.Equal(1, total);
        var stored = events.Single();
        Assert.Equal("package_upload_succeeded", stored.Event);
        Assert.Equal("default", stored.Feed);
        Assert.Equal("TestData", stored.PackageId);
        Assert.Equal("1.2.3", stored.PackageVersion);
        Assert.Equal(_app.Services.GetRequiredService<SystemTime>().UtcNow, stored.TimestampUtc);
    }

    private async Task<HttpStatusCode> PushAsync()
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StreamContent(TestResources.GetResourceStream(TestResources.Package)), "package", "package.nupkg");

        using var response = await _client.PutAsync("api/v2/package", content);
        return response.StatusCode;
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }
}
