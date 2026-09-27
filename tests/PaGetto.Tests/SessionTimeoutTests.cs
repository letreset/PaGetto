using System;
using PaGetto.Core.Authentication;
using PaGetto.Tests.Support;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// Authentication:SessionTimeoutMinutes sets the sliding lifetime of the sign-in cookie.
/// </summary>
public class SessionTimeoutTests
{
    private readonly ITestOutputHelper _output;

    public SessionTimeoutTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private TimeSpan CookieLifetime(string sessionTimeoutMinutes)
    {
        using var app = new PaGettoApplication(_output, null, dict =>
        {
            dict["Authentication:Mode"] = "Local";
            if (sessionTimeoutMinutes != null)
                dict["Authentication:SessionTimeoutMinutes"] = sessionTimeoutMinutes;
        });

        var options = app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(AuthenticationConstants.CookieScheme);
        Assert.True(options.SlidingExpiration);
        return options.ExpireTimeSpan;
    }

    [Fact]
    public void DefaultsTo60Minutes()
    {
        Assert.Equal(TimeSpan.FromMinutes(60), CookieLifetime(null));
    }

    [Fact]
    public void UsesTheConfiguredTimeout()
    {
        Assert.Equal(TimeSpan.FromMinutes(480), CookieLifetime("480"));
    }
}
