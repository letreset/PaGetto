using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using PaGetto.Core.Configuration;
using PaGetto.Core.Updates;
using Xunit;

namespace PaGetto.Core.Tests.Updates;

public class ReleaseCheckerTests
{
    public class CheckAsync : FactsBase
    {
        [Fact]
        public async Task ReadsTheLatestReleaseFromGitHub()
        {
            Handler.Release = """{ "tag_name": "v1.2.0", "html_url": "https://example.com/elsewhere" }""";

            await Target.CheckAsync(CancellationToken.None);

            var request = Assert.Single(Handler.Requests);
            Assert.Equal(GitHubReleaseClient.LatestReleaseUrl, request.RequestUri!.AbsoluteUri);
            Assert.Equal("PaGetto", request.Headers.UserAgent.ToString());
            Assert.Null(request.Headers.Authorization);

            var release = Target.GetNewerRelease("1.0.0");
            Assert.NotNull(release);
            Assert.Equal("1.2.0", release.Version.ToNormalizedString());
            Assert.Equal("https://github.com/letreset/PaGetto/releases/tag/v1.2.0", release.ReleaseNotesUrl.AbsoluteUri);
        }

        [Fact]
        public async Task SendsNothingWhenTurnedOff()
        {
            Options.Enabled = false;

            await Target.CheckAsync(CancellationToken.None);

            Assert.Empty(Handler.Requests);
        }

        [Fact]
        public async Task FailsSilentlyWithoutNetworkAccess()
        {
            Handler.Failure = new HttpRequestException("No such host is known.");

            await Target.CheckAsync(CancellationToken.None);

            Assert.Null(Target.GetNewerRelease("1.0.0"));
        }

        [Theory]
        [InlineData(HttpStatusCode.Forbidden, """{ "message": "API rate limit exceeded" }""")]
        [InlineData(HttpStatusCode.OK, "not json")]
        [InlineData(HttpStatusCode.OK, """{ "tag_name": "nightly" }""")]
        [InlineData(HttpStatusCode.OK, "[]")]
        public async Task KeepsTheReleaseFoundBeforeWhenACheckFails(HttpStatusCode status, string body)
        {
            Handler.Release = """{ "tag_name": "v1.2.0" }""";
            await Target.CheckAsync(CancellationToken.None);

            Handler.Status = status;
            Handler.Release = body;
            await Target.CheckAsync(CancellationToken.None);

            Assert.Equal("1.2.0", Target.GetNewerRelease("1.0.0")?.Version.ToNormalizedString());
        }
    }

    public class GetNewerRelease : FactsBase
    {
        [Theory]
        [InlineData("v1.2.0", "1.1.9", true)]
        [InlineData("v1.2.0", "1.2.0-rc.1", true)]
        [InlineData("v1.2.0", "1.2.0", false)]
        [InlineData("v1.2.0", "1.3.0", false)]
        [InlineData("v1.2.0", "1.3.0-rc.1", false)]
        [InlineData("1.2.0", "1.1.0", true)]
        [InlineData("v1.2.0", "not a version", false)]
        public async Task ComparesWithTheRunningVersion(string tag, string runningVersion, bool newer)
        {
            Handler.Release = $$"""{ "tag_name": "{{tag}}" }""";
            await Target.CheckAsync(CancellationToken.None);

            Assert.Equal(newer, Target.GetNewerRelease(runningVersion) != null);
        }

        [Fact]
        public void ReturnsNullBeforeTheFirstCheck()
        {
            Assert.Null(Target.GetNewerRelease("1.0.0"));
        }

        [Fact]
        public async Task ReturnsNullOnceTurnedOff()
        {
            Handler.Release = """{ "tag_name": "v1.2.0" }""";
            await Target.CheckAsync(CancellationToken.None);

            Options.Enabled = false;

            Assert.Null(Target.GetNewerRelease("1.0.0"));
        }
    }

    public class FactsBase
    {
        protected FactsBase()
        {
            var monitor = new Mock<IOptionsMonitor<UpdateCheckOptions>>();
            monitor.Setup(m => m.CurrentValue).Returns(() => Options);
            Target = new ReleaseChecker(new GitHubReleaseClient(Handler), monitor.Object, NullLogger<ReleaseChecker>.Instance);
        }

        protected UpdateCheckOptions Options { get; } = new();

        protected FakeGitHub Handler { get; } = new();

        protected ReleaseChecker Target { get; }
    }

    /// <summary>
    /// Answers every request with <see cref="Release"/>, or throws <see cref="Failure"/>.
    /// </summary>
    public class FakeGitHub : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public string Release { get; set; }

        public Exception Failure { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (Failure != null)
            {
                throw Failure;
            }

            return Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = new StringContent(Release ?? string.Empty, Encoding.UTF8, "application/json"),
            });
        }
    }
}
