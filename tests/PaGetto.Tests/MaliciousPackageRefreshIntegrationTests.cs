using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Entities;
using PaGetto.Core.MaliciousPackages;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// The local copy of the malicious packages, refreshed from a fake OSV data export.
/// </summary>
public class MaliciousPackageRefreshIntegrationTests : IDisposable
{
    private readonly FakeExport _export = new();
    private readonly PaGettoApplication _app;

    public MaliciousPackageRefreshIntegrationTests(ITestOutputHelper output)
    {
        _app = new PaGettoApplication(output, _export, dict =>
        {
            dict["MaliciousPackages:Enabled"] = "true";
            dict["MaliciousPackages:DataUrl"] = "https://osv-data.test/";
        });
    }

    [Fact]
    public async Task CopiesTheArchiveAndThenReadsOnlyTheChanges()
    {
        await _app.CreateNpmFeedAsync("proxy", "https://registry.test/");
        _export.Archive["MAL-2099-0001.json"] = Advisory("MAL-2099-0001", "evil-one", "2020-01-01T00:00:00.123456789Z");
        _export.Archive["MAL-2099-0002.json"] = Advisory("MAL-2099-0002", "Evil-Two", "2020-01-02T00:00:00Z");
        _export.Archive["GHSA-xxxx-yyyy-zzzz.json"] = Advisory("GHSA-xxxx-yyyy-zzzz", "not-malicious", "2020-01-03T00:00:00Z");

        Assert.Equal(1, await RunAsync());
        Assert.Equal(["evil-one", "evil-two"], await GetNamesAsync());

        // Not due again until the refresh interval has passed.
        Assert.Equal(0, await RunAsync());

        // One advisory withdrawn, one added: only they are read.
        _export.Archive.Clear();
        _export.Changes = """
            2020-01-05T00:00:00.000000001Z,MAL-2099-0003
            2020-01-04T00:00:00Z,MAL-2099-0002
            2020-01-03T00:00:00Z,GHSA-xxxx-yyyy-zzzz
            2020-01-02T00:00:00Z,MAL-2099-0002
            """;
        _export.Advisories["MAL-2099-0003"] = Advisory("MAL-2099-0003", "evil-three", "2020-01-05T00:00:00Z");
        _export.Advisories["MAL-2099-0002"] = Advisory("MAL-2099-0002", "Evil-Two", "2020-01-04T00:00:00Z", withdrawn: true);
        await MakeDueAsync();

        Assert.Equal(1, await RunAsync());
        Assert.Equal(["evil-one", "evil-three"], await GetNamesAsync());
        Assert.Single(_export.Requests, r => r == "/npm/all.zip");
    }

    [Fact]
    public async Task KeepsTheCopyWhenARefreshFails()
    {
        await _app.CreateNpmFeedAsync("proxy", "https://registry.test/");
        _export.Archive["MAL-2099-0001.json"] = Advisory("MAL-2099-0001", "evil-one", "2020-01-01T00:00:00Z");
        await RunAsync();

        _export.Fail = true;
        await MakeDueAsync();

        Assert.Equal(0, await RunAsync());
        Assert.Equal(["evil-one"], await GetNamesAsync());

        using var scope = _app.Services.CreateScope();
        var source = await scope.ServiceProvider.GetRequiredService<IContext>().MaliciousPackageSources.SingleAsync(s => s.Ecosystem == "npm");
        Assert.NotNull(source.LastError);
    }

    [Fact]
    public async Task RefreshesNothingWithoutAFeedThatMirrorsAndBlocks()
    {
        await _app.CreateNpmFeedAsync("hosted");
        await _app.CreateNpmFeedAsync("proxy", "https://registry.test/", f => f.BlockMaliciousPackages = false);

        await RunAsync();
        Assert.DoesNotContain(_export.Requests, r => r.StartsWith("/npm/", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        _app.Dispose();
    }

    private static string Advisory(string id, string name, string modified, bool withdrawn = false)
    {
        return $$"""
            {
              "id": "{{id}}",
              "modified": "{{modified}}",
              {{(withdrawn ? "\"withdrawn\": \"2020-01-04T00:00:00Z\"," : "")}}
              "summary": "Malicious code in {{name}} (npm)",
              "affected": [{
                "package": { "ecosystem": "npm", "name": "{{name}}" },
                "ranges": [{ "type": "SEMVER", "events": [{ "introduced": "0" }] }]
              }]
            }
            """;
    }

    private async Task<int> RunAsync()
    {
        using var scope = _app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MaliciousPackageUpdater>().RunAsync(CancellationToken.None);
    }

    private async Task<string[]> GetNamesAsync()
    {
        using var scope = _app.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IContext>().MaliciousPackages
            .Where(p => p.Ecosystem == "npm")
            .Select(p => p.Name)
            .OrderBy(n => n)
            .ToArrayAsync();
    }

    private async Task MakeDueAsync()
    {
        using var scope = _app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IContext>().MaliciousPackageSources
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RefreshedAtUtc, (DateTime?)null).SetProperty(x => x.AttemptedAtUtc, (DateTime?)null));
    }

    private sealed class FakeExport : HttpMessageHandler
    {
        public Dictionary<string, string> Archive { get; } = [];
        public Dictionary<string, string> Advisories { get; } = [];
        public string Changes { get; set; } = string.Empty;
        public bool Fail { get; set; }
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.Host != "osv-data.test")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            lock (Requests)
            {
                Requests.Add(uri.AbsolutePath);
            }

            if (Fail)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            var path = uri.AbsolutePath;
            if (path == "/npm/all.zip")
            {
                using var buffer = new MemoryStream();
                using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (var (name, json) in Archive)
                    {
                        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8);
                        writer.Write(json);
                    }
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(buffer.ToArray()) });
            }

            if (path == "/npm/modified_id.csv")
            {
                var lines = string.Join('\n', Changes.Split('\n').Select(l => l.Trim()));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(lines) });
            }

            var id = path.StartsWith("/npm/", StringComparison.Ordinal) ? Path.GetFileNameWithoutExtension(path) : null;
            return Task.FromResult(id != null && Advisories.TryGetValue(id, out var advisory)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(advisory) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
