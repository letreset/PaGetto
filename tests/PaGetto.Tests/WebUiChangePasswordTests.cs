using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Authentication;
using PaGetto.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// Account > Change password, and the forced password change of the default administrator.
/// </summary>
public class WebUiChangePasswordTests
{
    public abstract class FactsBase : IDisposable
    {
        protected const string DefaultPassword = InitialAdminSeeder.DefaultPassword;
        protected const string NewPassword = "BrandNewPassword456!";

        protected readonly PaGettoApplication _app;

        protected FactsBase(ITestOutputHelper output)
        {
            _app = new PaGettoApplication(output, null, dict =>
            {
                dict["Authentication:Mode"] = "Local";
            });
        }

        protected async Task SeedDefaultAdminAsync()
        {
            using var scope = _app.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<InitialAdminSeeder>().SeedAsync(CancellationToken.None);
        }

        protected static Task<HttpResponseMessage> ChangePasswordAsync(
            WebUiSession session, string currentPassword, string newPassword, string confirmPassword = null)
        {
            return session.PostFormAsync("/Account/ChangePassword", null, new Dictionary<string, string>
            {
                { "CurrentPassword", currentPassword },
                { "NewPassword", newPassword },
                { "ConfirmPassword", confirmPassword ?? newPassword },
            });
        }

        public void Dispose()
        {
            _app.Dispose();
        }
    }

    public class ForcedChange : FactsBase
    {
        public ForcedChange(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public async Task SignInLandsOnChangePassword()
        {
            await SeedDefaultAdminAsync();

            using var session = await WebUiSession.SignInAsync(_app, InitialAdminSeeder.DefaultUsername, DefaultPassword);
            using var response = await session.Client.GetAsync("/Account/ChangePassword");
            var body = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Choose a new password to continue", body);
        }

        [Theory]
        [InlineData("/")]
        [InlineData("/Admin/Accounts")]
        [InlineData("/Account/Tokens")]
        public async Task RedirectsOtherPagesToChangePassword(string path)
        {
            await SeedDefaultAdminAsync();

            using var session = await WebUiSession.SignInAsync(_app, InitialAdminSeeder.DefaultUsername, DefaultPassword);
            using var response = await session.Client.GetAsync(path);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("/Account/ChangePassword", response.Headers.Location?.OriginalString);
        }

        [Fact]
        public async Task RejectsOtherPosts()
        {
            await SeedDefaultAdminAsync();

            using var session = await WebUiSession.SignInAsync(_app, InitialAdminSeeder.DefaultUsername, DefaultPassword);
            using var response = await session.Client.PostAsync("/Account/Tokens?handler=Create", null);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task ChangingThePasswordUnlocksTheUI()
        {
            await SeedDefaultAdminAsync();

            using var session = await WebUiSession.SignInAsync(_app, InitialAdminSeeder.DefaultUsername, DefaultPassword);
            using var change = await ChangePasswordAsync(session, DefaultPassword, NewPassword);
            Assert.Equal(HttpStatusCode.Redirect, change.StatusCode);

            using var accounts = await session.Client.GetAsync("/Admin/Accounts");
            Assert.Equal(HttpStatusCode.OK, accounts.StatusCode);

            using var again = await WebUiSession.SignInAsync(_app, InitialAdminSeeder.DefaultUsername, NewPassword);
        }
    }

    public class Validation : FactsBase
    {
        public Validation(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public async Task RejectsWrongCurrentPassword()
        {
            await SeedDefaultAdminAsync();

            using var session = await WebUiSession.SignInAsync(_app, InitialAdminSeeder.DefaultUsername, DefaultPassword);
            using var change = await ChangePasswordAsync(session, "wrong-password", NewPassword);

            Assert.Equal(HttpStatusCode.OK, change.StatusCode);
            Assert.Contains("current password is incorrect", await change.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task RejectsShortPassword()
        {
            await SeedDefaultAdminAsync();

            using var session = await WebUiSession.SignInAsync(_app, InitialAdminSeeder.DefaultUsername, DefaultPassword);
            using var change = await ChangePasswordAsync(session, DefaultPassword, "short");

            Assert.Contains("at least 12 characters", await change.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task RejectsMismatchedConfirmation()
        {
            await SeedDefaultAdminAsync();

            using var session = await WebUiSession.SignInAsync(_app, InitialAdminSeeder.DefaultUsername, DefaultPassword);
            using var change = await ChangePasswordAsync(session, DefaultPassword, NewPassword, "SomethingElse123!");

            Assert.Contains("don&#x27;t match", await change.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task RegularUserCanChangePassword()
        {
            const string password = "LocalPassword123!";
            await WebUiSession.SeedLocalUserAsync(_app, "alice", password);

            using var session = await WebUiSession.SignInAsync(_app, "alice", password);
            using var change = await ChangePasswordAsync(session, password, NewPassword);
            Assert.Equal(HttpStatusCode.Redirect, change.StatusCode);

            using var again = await WebUiSession.SignInAsync(_app, "alice", NewPassword);
        }
    }
}
