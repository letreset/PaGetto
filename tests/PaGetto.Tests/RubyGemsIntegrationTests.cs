using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Tests.Support;
using PaGetto.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// The gem server of a RubyGems feed, in Legacy mode without credentials (open access).
/// </summary>
public class RubyGemsIntegrationTests : IDisposable
{
    private const string Feed = "ruby";

    private readonly ITestOutputHelper _output;
    private readonly PaGettoApplication _app;
    private readonly HttpClient _client;

    public RubyGemsIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _app = new PaGettoApplication(output);
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task PushedGemIsServedToBundlerAndGem()
    {
        await _app.CreateRubyGemsFeedAsync(Feed);
        var gem = RubyGemBuilder.Create("my_gem", "1.0.0", readme: "# My gem");

        using var push = await _client.PushGemAsync(Feed, gem);

        Assert.Equal(HttpStatusCode.OK, push.StatusCode);
        Assert.Equal("Successfully registered gem: my_gem (1.0.0)", await push.Content.ReadAsStringAsync());

        var sha256 = Convert.ToHexStringLower(SHA256.HashData(gem));
        using var info = await _client.GetAsync(RubyGemsTestClient.SourceUrl(Feed) + "info/my_gem");
        var infoBytes = await info.Content.ReadAsByteArrayAsync();
        var infoMd5 = Convert.ToHexStringLower(MD5.HashData(infoBytes));

        Assert.Equal(HttpStatusCode.OK, info.StatusCode);
        Assert.Equal($"---\n1.0.0 rake:~> 13.0|checksum:{sha256},ruby:>= 3.0\n", System.Text.Encoding.UTF8.GetString(infoBytes));
        Assert.Equal($"\"{infoMd5}\"", info.Headers.ETag?.Tag);
        Assert.Equal($"sha-256=:{Convert.ToBase64String(SHA256.HashData(infoBytes))}:", info.Headers.GetValues("Repr-Digest").Single());

        // Bundler compares the MD5 of /versions with that of the info file it has.
        var versions = await _client.GetStringAsync(RubyGemsTestClient.SourceUrl(Feed) + "versions");
        Assert.StartsWith("created_at: 20", versions);
        Assert.EndsWith($"\n---\nmy_gem 1.0.0 {infoMd5}\n", versions);
        Assert.Equal("---\nmy_gem\n", await _client.GetStringAsync(RubyGemsTestClient.SourceUrl(Feed) + "names"));

        // gem install checks for /versions with HEAD, reads the gemspec and downloads the gem.
        using (var head = await _client.SendAsync(new HttpRequestMessage(HttpMethod.Head, RubyGemsTestClient.SourceUrl(Feed) + "versions")))
        {
            Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        }

        var quickSpec = await _client.GetByteArrayAsync(RubyGemsTestClient.SourceUrl(Feed) + "quick/Marshal.4.8/my_gem-1.0.0.gemspec.rz");
        var fields = ReadQuickSpec(quickSpec);
        Assert.Equal("my_gem", fields[2]);

        using var download = await _client.GetAsync(RubyGemsTestClient.SourceUrl(Feed) + "gems/my_gem-1.0.0.gem");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(gem, await download.Content.ReadAsByteArrayAsync());

        var stored = await GetVersionAsync("1.0.0");
        Assert.Equal("my_gem", stored.Artifact.Name);
        Assert.Equal("A test gem.", stored.Description);
        Assert.True(stored.HasReadme);
        Assert.True(stored.Listed);
        Assert.Null(stored.CachedFrom);
        Assert.Equal(1, stored.Downloads);
        var file = Assert.Single(stored.Files);
        Assert.Equal("my_gem-1.0.0.gem", file.FileName);
        Assert.Equal(sha256, file.Sha256);
    }

    [Fact]
    public async Task EachPlatformOfAVersionHasItsOwnGem()
    {
        await _app.CreateRubyGemsFeedAsync(Feed);
        var java = RubyGemBuilder.Create("my_gem", "1.0.0", "java", dependencies: []);

        using (await _client.PushGemAsync(Feed, "my_gem", "1.0.0"))
        {
        }

        using var push = await _client.PushGemAsync(Feed, java);

        Assert.Equal(HttpStatusCode.OK, push.StatusCode);
        Assert.Equal("Successfully registered gem: my_gem (1.0.0-java)", await push.Content.ReadAsStringAsync());

        var lines = await _client.GetIndexLinesAsync(RubyGemsTestClient.SourceUrl(Feed) + "info/my_gem");
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("1.0.0 rake:~> 13.0|", lines[0]);
        Assert.StartsWith("1.0.0-java |checksum:", lines[1]);

        var versions = await _client.GetIndexLinesAsync(RubyGemsTestClient.SourceUrl(Feed) + "versions");
        Assert.StartsWith("my_gem 1.0.0,1.0.0-java ", Assert.Single(versions));

        Assert.Equal(java, await _client.GetByteArrayAsync(RubyGemsTestClient.SourceUrl(Feed) + "gems/my_gem-1.0.0-java.gem"));
        Assert.Equal("java", ReadQuickSpec(await _client.GetByteArrayAsync(RubyGemsTestClient.SourceUrl(Feed) + "quick/Marshal.4.8/my_gem-1.0.0-java.gemspec.rz"))[16]);

        var stored = await GetVersionAsync("1.0.0");
        Assert.Equal(new[] { "my_gem-1.0.0-java.gem", "my_gem-1.0.0.gem" }, stored.Files.Select(f => f.FileName).Order());
        Assert.Equal(stored.Files.Sum(f => f.Size), stored.Size);

        using var missing = await _client.GetAsync(RubyGemsTestClient.SourceUrl(Feed) + "gems/my_gem-1.0.0-x86_64-linux.gem");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task GemKeepsTheNameItWasFirstPushedUnder()
    {
        await _app.CreateRubyGemsFeedAsync(Feed);
        using (await _client.PushGemAsync(Feed, "My_Gem", "1.0.0"))
        {
        }

        using var response = await _client.PushGemAsync(Feed, "my_gem", "2.0.0");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("named My_Gem", await response.Content.ReadAsStringAsync());

        // The gem is found by its name in any case, and listed under the name it was pushed with.
        Assert.Single(await _client.GetIndexLinesAsync(RubyGemsTestClient.SourceUrl(Feed) + "info/my_gem"));
        Assert.StartsWith("My_Gem 1.0.0 ", Assert.Single(await _client.GetIndexLinesAsync(RubyGemsTestClient.SourceUrl(Feed) + "versions")));
        using var download = await _client.GetAsync(RubyGemsTestClient.SourceUrl(Feed) + "gems/My_Gem-1.0.0.gem");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
    }

    [Fact]
    public async Task PushingTheSameGemAgainChangesNothing()
    {
        await _app.CreateRubyGemsFeedAsync(Feed);
        var gem = RubyGemBuilder.Create("my_gem", "1.0.0");

        using (await _client.PushGemAsync(Feed, gem))
        {
        }

        using var again = await _client.PushGemAsync(Feed, gem);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
    }

    [Theory]
    [InlineData(PackageOverwriteAllowed.False, "1.0.0", HttpStatusCode.Conflict)]
    [InlineData(PackageOverwriteAllowed.PrereleaseOnly, "1.0.0", HttpStatusCode.Conflict)]
    [InlineData(PackageOverwriteAllowed.PrereleaseOnly, "1.0.0.rc1", HttpStatusCode.OK)]
    [InlineData(PackageOverwriteAllowed.True, "1.0.0", HttpStatusCode.OK)]
    public async Task OtherContentForAGemFollowsTheOverwritePolicy(PackageOverwriteAllowed policy, string version, HttpStatusCode expected)
    {
        await _app.CreateRubyGemsFeedAsync(Feed, configure: f => f.AllowPackageOverwrites = policy);
        using (await _client.PushGemAsync(Feed, "my_gem", version))
        {
        }

        var other = RubyGemBuilder.Create("my_gem", version, readme: "# Changed");
        using var response = await _client.PushGemAsync(Feed, other);

        Assert.Equal(expected, response.StatusCode);
        var served = await _client.GetByteArrayAsync(RubyGemsTestClient.SourceUrl(Feed) + $"gems/my_gem-{version}.gem");
        if (expected == HttpStatusCode.OK)
        {
            Assert.Equal(other, served);
            Assert.Contains(Convert.ToHexStringLower(SHA256.HashData(other)), await _client.GetStringAsync(RubyGemsTestClient.SourceUrl(Feed) + "info/my_gem"));
        }
        else
        {
            Assert.Contains("Repushing of gem versions is not allowed", await response.Content.ReadAsStringAsync());
            Assert.NotEqual(other, served);
        }
    }

    [Fact]
    public async Task YankingTakesTheVersionOutOfTheIndexButNotOutOfTheFeed()
    {
        await _app.CreateRubyGemsFeedAsync(Feed);
        using (await _client.PushGemAsync(Feed, "my_gem", "1.0.0"))
        {
        }

        using (await _client.PushGemAsync(Feed, "my_gem", "1.1.0"))
        {
        }

        using var yank = await _client.YankGemAsync(Feed, "my_gem", "1.1.0");

        Assert.Equal(HttpStatusCode.OK, yank.StatusCode);
        Assert.Equal("Successfully yanked gem: my_gem (1.1.0)", await yank.Content.ReadAsStringAsync());
        Assert.StartsWith("1.0.0 ", Assert.Single(await _client.GetIndexLinesAsync(RubyGemsTestClient.SourceUrl(Feed) + "info/my_gem")));
        Assert.StartsWith("my_gem 1.0.0,-1.1.0 ", Assert.Single(await _client.GetIndexLinesAsync(RubyGemsTestClient.SourceUrl(Feed) + "versions")));
        Assert.False((await GetVersionAsync("1.1.0")).Listed);

        using (var download = await _client.GetAsync(RubyGemsTestClient.SourceUrl(Feed) + "gems/my_gem-1.1.0.gem"))
        {
            Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        }

        using var unknown = await _client.YankGemAsync(Feed, "my_gem", "9.9.9");
        using var otherPlatform = await _client.YankGemAsync(Feed, "my_gem", "1.0.0", "java");
        using var ruby = await _client.YankGemAsync(Feed, "my_gem", "1.0.0", "ruby");

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherPlatform.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ruby.StatusCode);
    }

    [Theory]
    [InlineData("not a gem", "tar")]
    [InlineData("invalid name", "not a valid gem name")]
    [InlineData("invalid version", "not a gem version")]
    [InlineData("invalid platform", "not a platform")]
    public async Task InvalidGemIsRejectedWithAMessage(string kind, string message)
    {
        await _app.CreateRubyGemsFeedAsync(Feed);
        var gem = kind switch
        {
            "not a gem" => Enumerable.Repeat((byte)'x', 2048).ToArray(),
            "invalid name" => RubyGemBuilder.Create("my gem", "1.0.0"),
            "invalid version" => RubyGemBuilder.CreateFromGemspec(RubyGemBuilder.Gemspec("my_gem", "1.0.0").Replace("version: 1.0.0", "version: 1.0-beta")),
            _ => RubyGemBuilder.Create("my_gem", "1.0.0", "x86/linux"),
        };

        using var response = await _client.PushGemAsync(Feed, gem);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains(message, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ReadOnlyFeedRefusesToPushAndYank()
    {
        await _app.CreateRubyGemsFeedAsync(Feed, configure: f => f.IsReadOnlyMode = true);

        using var push = await _client.PushGemAsync(Feed, "my_gem", "1.0.0");
        using var yank = await _client.YankGemAsync(Feed, "my_gem", "1.0.0");

        Assert.Equal(HttpStatusCode.Forbidden, push.StatusCode);
        Assert.Contains("read-only", await push.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, yank.StatusCode);
    }

    [Fact]
    public async Task GemLargerThanTheFeedLimitIsRejected()
    {
        await _app.CreateRubyGemsFeedAsync(Feed, configure: f => f.MaxPackageSizeMiB = 1);

        // Random text doesn't compress below 1 MiB.
        var readme = Convert.ToBase64String(RandomNumberGenerator.GetBytes(2 * 1024 * 1024));
        using var response = await _client.PushGemAsync(Feed, RubyGemBuilder.Create("my_gem", "1.0.0", readme: readme));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("1 MiB", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ConfiguredApiKeyIsNeededToPush()
    {
        using var app = new PaGettoApplication(_output, null, dict => dict["Authentication:ApiKeys:0:Key"] = "secret-key");
        using var client = app.CreateClient();
        await app.CreateRubyGemsFeedAsync(Feed);
        var gem = RubyGemBuilder.Create("my_gem", "1.0.0");

        using var anonymous = await client.PushGemAsync(Feed, gem);
        using var wrong = await client.PushGemAsync(Feed, gem, apiKey: "wrong-key");
        using var right = await client.PushGemAsync(Feed, gem, apiKey: "secret-key");
        using var yank = await client.YankGemAsync(Feed, "my_gem", "1.0.0", apiKey: "secret-key");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
        Assert.Equal(HttpStatusCode.OK, yank.StatusCode);
    }

    [Fact]
    public async Task IndexFilesAnswerConditionalAndRangeRequests()
    {
        await _app.CreateRubyGemsFeedAsync(Feed);
        using (await _client.PushGemAsync(Feed, "my_gem", "1.0.0"))
        {
        }

        var url = RubyGemsTestClient.SourceUrl(Feed) + "versions";
        using var full = await _client.GetAsync(url);
        var content = await full.Content.ReadAsByteArrayAsync();

        // Bundler asks whether its copy is still current, and else for the bytes after it.
        using var conditional = new HttpRequestMessage(HttpMethod.Get, url);
        conditional.Headers.IfNoneMatch.Add(full.Headers.ETag);
        using var notModified = await _client.SendAsync(conditional);

        using var ranged = new HttpRequestMessage(HttpMethod.Get, url);
        ranged.Headers.Range = new RangeHeaderValue(10, null);
        using var partial = await _client.SendAsync(ranged);

        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
        Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
        Assert.Equal(content[10..], await partial.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task UnknownGemsAndFilesAreNotFound()
    {
        await _app.CreateRubyGemsFeedAsync(Feed);

        foreach (var path in new[] { "info/missing", "info/not%20a%20name", "gems/missing-1.0.0.gem", "gems/missing.tgz", "quick/Marshal.4.8/missing-1.0.0.gemspec.rz", "specs.4.8.gz" })
        {
            using var response = await _client.GetAsync(RubyGemsTestClient.SourceUrl(Feed) + path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        Assert.Equal("---\n", await _client.GetStringAsync(RubyGemsTestClient.SourceUrl(Feed) + "names"));
    }

    [Theory]
    [InlineData("GET", "v3/index.json")]
    [InlineData("PUT", "api/v2/package")]
    [InlineData("GET", "api/packages/my_gem")]
    [InlineData("GET", "npm/my_gem")]
    [InlineData("GET", "cargo/index/config.json")]
    [InlineData("GET", "go/example.com/x/@v/list")]
    [InlineData("GET", "upload")]
    public async Task OtherApisAreNotFoundOnARubyGemsFeed(string method, string path)
    {
        await _app.CreateRubyGemsFeedAsync(Feed);

        using var request = new HttpRequestMessage(new HttpMethod(method), $"feeds/{Feed}/{path}");
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("rubygems/versions")]
    [InlineData("rubygems/info/my_gem")]
    [InlineData("rubygems/gems/my_gem-1.0.0.gem")]
    public async Task RubyGemsApiIsNotFoundOnANuGetFeed(string path)
    {
        await _app.CreateFeedAsync("nuget");

        using var named = await _client.GetAsync($"feeds/nuget/{path}");
        using var defaultFeed = await _client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, named.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, defaultFeed.StatusCode);
    }

    [Fact]
    public async Task WebUiShowsTheFeedAndItsGems()
    {
        await _app.CreateRubyGemsFeedAsync(Feed);
        using (await _client.PushGemAsync(Feed, RubyGemBuilder.Create("My_Gem", "1.0.0", readme: "# Hello from the readme\n\n<script>alert(1)</script>")))
        {
        }

        using (await _client.PushGemAsync(Feed, RubyGemBuilder.Create("My_Gem", "1.0.0", "java")))
        {
        }

        var index = await _client.GetStringAsync($"feeds/{Feed}/");
        Assert.Contains("Ruby feed", index);
        Assert.Contains("<span class=\"bgt-badge ms-1\">Ruby</span>", index);
        Assert.Contains("My_Gem", index);
        Assert.Contains("A test gem.", index);
        Assert.Contains($"bundle add My_Gem --version &quot;1.0.0&quot; --source &quot;http://localhost/feeds/{Feed}/rubygems/&quot;", index);

        // The page is found by the lowercased name too.
        var details = await _client.GetStringAsync($"feeds/{Feed}/artifacts/my_gem");
        Assert.Contains("<h1>My_Gem</h1>", details);
        Assert.Contains("Hello from the readme", details);
        Assert.DoesNotContain("<script>alert(1)</script>", details);
        Assert.Contains("https://example.com/My_Gem/source", details);
        Assert.Contains("Ruby &gt;= 3.0", details);
        Assert.Contains("rake", details);
        Assert.Contains("MIT", details);
        Assert.Contains($"http://localhost/feeds/{Feed}/rubygems/gems/My_Gem-1.0.0-java.gem", details);
        Assert.Contains($"http://localhost/feeds/{Feed}/rubygems/gems/My_Gem-1.0.0.gem", details);

        var connect = await _client.GetStringAsync($"feeds/{Feed}/connect");
        Assert.Contains($"http://localhost/feeds/{Feed}/rubygems/", connect);
        Assert.Contains($"gem push my_gem-1.0.0.gem --host http://localhost/feeds/{Feed}/rubygems", connect);
    }

    public void Dispose()
    {
        _client.Dispose();
        _app.Dispose();
    }

    /// <summary>
    /// The fields of a <c>quick/Marshal.4.8</c> gemspec, in the order of <c>Gem::Specification#_dump</c>.
    /// </summary>
    internal static System.Collections.Generic.List<object> ReadQuickSpec(byte[] quickSpec)
    {
        using var zlib = new ZLibStream(new MemoryStream(quickSpec), CompressionMode.Decompress);
        using var marshal = new MemoryStream();
        zlib.CopyTo(marshal);

        var spec = (RubyMarshalReader.RubyObject)RubyMarshalReader.Read(marshal.ToArray());
        return (System.Collections.Generic.List<object>)RubyMarshalReader.Read((byte[])spec.Data);
    }

    private async Task<ArtifactVersion> GetVersionAsync(string version)
    {
        using var scope = _app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();

        return await context.ArtifactVersions
            .AsNoTracking()
            .Include(v => v.Artifact)
            .Include(v => v.Files)
            .SingleAsync(v => v.Version == version);
    }
}
