using System.Net;
using PaGetto.Core.Configuration;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;
using Xunit;

namespace PaGetto.Tests;

public class ConfigurePaGettoServerTests
{
    private static ForwardedHeadersOptions ConfigureForwardedHeaders(ProxyTrustOptions trust)
    {
        var target = new ConfigurePaGettoServer(Options.Create(new PaGettoOptions { ForwardedHeaders = trust }));
        var options = new ForwardedHeadersOptions();
        target.Configure(options);
        return options;
    }

    public class ConfigureForwardedHeadersOptions
    {
        [Fact]
        public void TrustsEveryProxyByDefault()
        {
            var options = ConfigureForwardedHeaders(new ProxyTrustOptions());

            Assert.Empty(options.KnownProxies);
            Assert.Empty(options.KnownIPNetworks);
        }

        [Fact]
        public void TrustsOnlyConfiguredProxies()
        {
            var options = ConfigureForwardedHeaders(new ProxyTrustOptions
            {
                KnownProxies = ["10.0.0.5"],
                KnownNetworks = ["192.168.0.0/16"],
            });

            Assert.Equal(IPAddress.Parse("10.0.0.5"), Assert.Single(options.KnownProxies));
            Assert.Equal(System.Net.IPNetwork.Parse("192.168.0.0/16"), Assert.Single(options.KnownIPNetworks));
        }
    }

    public class ConfigureAntiforgeryOptions
    {
        [Theory]
        [InlineData(null, "/")]
        [InlineData("", "/")]
        [InlineData("/base", "/")]
        public void PinsTheCookieToTheRootPath(string pathBase, string expected)
        {
            var target = new ConfigurePaGettoServer(Options.Create(new PaGettoOptions { PathBase = pathBase }));
            var options = new AntiforgeryOptions();

            target.Configure(options);

            Assert.Equal(expected, options.Cookie.Path);
        }
    }
}
