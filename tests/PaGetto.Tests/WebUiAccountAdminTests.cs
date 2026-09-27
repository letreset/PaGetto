using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Authentication;
using PaGetto.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace PaGetto.Tests;

/// <summary>
/// Administrator actions on Admin > Accounts.
/// </summary>
public class WebUiAccountAdminTests
{
    public abstract class FactsBase : IDisposable
    {
        protected const string Password = "LocalPassword123!";
        protected const string NewPassword = "BrandNewPassword456!";

        protected readonly PaGettoApplication _app;

        protected FactsBase(ITestOutputHelper output)
        {
            _app = new PaGettoApplication(output, null, dict =>
            {
                dict["Authentication:Mode"] = "Local";
            });
        }

        public void Dispose()
        {
            _app.Dispose();
        }
    }

    public class Create : FactsBase
    {
        public Create(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public async Task RejectsUsernameThatDiffersOnlyInCase()
        {
            await WebUiSession.SeedLocalUserAsync(_app, "admin", Password, isAdmin: true);
            await WebUiSession.SeedLocalUserAsync(_app, "alice", Password);

            using var admin = await WebUiSession.SignInAsync(_app, "admin", Password);
            using var response = await admin.PostFormAsync("/Admin/Accounts", "Create", new Dictionary<string, string>
            {
                { "NewUsername", "ALICE" },
                { "NewPassword", NewPassword },
            });

            Assert.Contains("already exists", await response.Content.ReadAsStringAsync());
            using var scope = _app.Services.CreateScope();
            var users = await scope.ServiceProvider.GetRequiredService<IUserService>().GetAllUsersAsync(CancellationToken.None);
            Assert.DoesNotContain(users, u => u.Username == "ALICE");
        }

        [Fact]
        public async Task SignInIgnoresUsernameCase()
        {
            await WebUiSession.SeedLocalUserAsync(_app, "alice", Password);

            using var session = await WebUiSession.SignInAsync(_app, "Alice", Password);
        }
    }

    public class ActionFeedback : FactsBase
    {
        public ActionFeedback(ITestOutputHelper output) : base(output)
        {
        }

        [Theory]
        [InlineData("ToggleEnabled", "isEnabled", "True", "Account &#x27;bob&#x27; disabled.")]
        [InlineData("ToggleEnabled", "isEnabled", "False", "Account &#x27;bob&#x27; enabled.")]
        [InlineData("ToggleCanLoginToUI", "canLoginToUI", "True", "Web sign-in disabled for &#x27;bob&#x27;.")]
        [InlineData("ToggleCanLoginToUI", "canLoginToUI", "False", "Web sign-in allowed for &#x27;bob&#x27;.")]
        [InlineData("ToggleAdmin", "isAdmin", "False", "&#x27;bob&#x27; is now an administrator.")]
        [InlineData("ToggleAdmin", "isAdmin", "True", "Administrator role removed from &#x27;bob&#x27;.")]
        public async Task ShowsTheResultOnceAfterTheRedirect(string handler, string field, string current, string message)
        {
            await WebUiSession.SeedLocalUserAsync(_app, "admin", Password, isAdmin: true);
            var bob = await WebUiSession.SeedLocalUserAsync(_app, "bob", Password);

            using var admin = await WebUiSession.SignInAsync(_app, "admin", Password);
            using var response = await admin.PostFormAsync("/Admin/Accounts", handler, new Dictionary<string, string>
            {
                { "userId", bob.Id.ToString() },
                { field, current },
            });

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains(message, await admin.GetStringAsync("/Admin/Accounts"));
            Assert.DoesNotContain(message, await admin.GetStringAsync("/Admin/Accounts"));
        }
    }

    public class AdminRights : FactsBase
    {
        public AdminRights(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public async Task MakesAnotherLocalAccountAnAdministrator()
        {
            await WebUiSession.SeedLocalUserAsync(_app, "admin", Password, isAdmin: true);
            var bob = await WebUiSession.SeedLocalUserAsync(_app, "bob", Password);

            using var admin = await WebUiSession.SignInAsync(_app, "admin", Password);
            using var response = await admin.PostFormAsync("/Admin/Accounts", "ToggleAdmin", new Dictionary<string, string>
            {
                { "userId", bob.Id.ToString() },
                { "isAdmin", "False" },
            });

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.True((await FindAsync(bob.Id)).IsAdmin);
        }

        [Theory]
        [InlineData("ToggleEnabled", "isEnabled")]
        [InlineData("ToggleCanLoginToUI", "canLoginToUI")]
        [InlineData("ToggleAdmin", "isAdmin")]
        public async Task RefusesToLockTheSignedInAdministratorOut(string handler, string field)
        {
            var self = await WebUiSession.SeedLocalUserAsync(_app, "admin", Password, isAdmin: true);

            using var admin = await WebUiSession.SignInAsync(_app, "admin", Password);
            using var response = await admin.PostFormAsync("/Admin/Accounts", handler, new Dictionary<string, string>
            {
                { "userId", self.Id.ToString() },
                { field, "True" },
            });

            Assert.Contains("your own administrator access", await response.Content.ReadAsStringAsync());
            var unchanged = await FindAsync(self.Id);
            Assert.True(unchanged.IsEnabled && unchanged.CanLoginToUI && unchanged.IsAdmin);
        }

        [Fact]
        public async Task OwnRowHasNoLockOutButtons()
        {
            // The administrator is the only account, so any of these handlers would be on their own row.
            await WebUiSession.SeedLocalUserAsync(_app, "admin", Password, isAdmin: true);

            using var admin = await WebUiSession.SignInAsync(_app, "admin", Password);
            var body = await admin.GetStringAsync("/Admin/Accounts");

            Assert.DoesNotContain("handler=ToggleEnabled", body);
            Assert.DoesNotContain("handler=ToggleCanLoginToUI", body);
            Assert.DoesNotContain("handler=ToggleAdmin", body);
        }

        [Fact]
        public async Task ShowsAndUnlocksLockedAccounts()
        {
            await WebUiSession.SeedLocalUserAsync(_app, "admin", Password, isAdmin: true);
            var bob = await WebUiSession.SeedLocalUserAsync(_app, "bob", Password);
            using (var scope = _app.Services.CreateScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<IUserService>();
                for (var i = 0; i < 5; i++)
                {
                    await users.RecordFailedLoginAsync(bob.Id, CancellationToken.None);
                }
            }

            using var admin = await WebUiSession.SignInAsync(_app, "admin", Password);
            var body = await admin.GetStringAsync("/Admin/Accounts");
            Assert.Contains("Locked until", body);

            using var response = await admin.PostFormAsync("/Admin/Accounts", "Unlock", new Dictionary<string, string>
            {
                { "userId", bob.Id.ToString() },
            });

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Null((await FindAsync(bob.Id)).LockedUntilUtc);
            using var bobSession = await WebUiSession.SignInAsync(_app, "bob", Password);
        }

        private async Task<PaGetto.Core.Entities.User> FindAsync(Guid userId)
        {
            using var scope = _app.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IUserService>().FindByIdAsync(userId, CancellationToken.None);
        }
    }

    public class DeleteConfirmation : FactsBase
    {
        public DeleteConfirmation(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public async Task PutsUsernameIntoAnAttributeInsteadOfScript()
        {
            const string username = "x'+(document.title='pwned')+'\"<\\";
            await WebUiSession.SeedLocalUserAsync(_app, "admin", Password, isAdmin: true);
            var target = await WebUiSession.SeedLocalUserAsync(_app, username, Password);
            using (var scope = _app.Services.CreateScope())
            {
                await scope.ServiceProvider.GetRequiredService<IUserService>()
                    .SetEnabledAsync(target.Id, false, CancellationToken.None);
            }

            using var admin = await WebUiSession.SignInAsync(_app, "admin", Password);
            var body = await admin.GetStringAsync("/Admin/Accounts");

            // The delete dialog takes the username from this attribute and shows it as text.
            Assert.DoesNotContain("onsubmit=", body);
            Assert.Contains(
                "data-username=\"x&#x27;&#x2B;(document.title=&#x27;pwned&#x27;)&#x2B;&#x27;&quot;&lt;\\\"",
                body);
        }
    }

    public class Edit : FactsBase
    {
        public Edit(ITestOutputHelper output) : base(output)
        {
        }

        private static Dictionary<string, string> Form(Guid userId, string username, string displayName = "", string email = "")
        {
            return new Dictionary<string, string>
            {
                { "userId", userId.ToString() },
                { "username", username },
                { "displayName", displayName },
                { "email", email },
            };
        }

        [Fact]
        public async Task RenamesAccount()
        {
            await WebUiSession.SeedLocalUserAsync(_app, "admin", Password, isAdmin: true);
            var bob = await WebUiSession.SeedLocalUserAsync(_app, "bob", Password);

            using var admin = await WebUiSession.SignInAsync(_app, "admin", Password);
            Assert.Contains("handler=Edit", await admin.GetStringAsync("/Admin/Accounts"));

            using var response = await admin.PostFormAsync(
                "/Admin/Accounts", "Edit", Form(bob.Id, "robert", "Robert Smith", "robert@example.com"));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

            using var scope = _app.Services.CreateScope();
            var updated = await scope.ServiceProvider.GetRequiredService<IUserService>().FindByIdAsync(bob.Id, CancellationToken.None);
            Assert.Equal("robert", updated.Username);
            Assert.Equal("Robert Smith", updated.DisplayName);
            Assert.Equal("robert@example.com", updated.Email);

            using var robert = await WebUiSession.SignInAsync(_app, "robert", Password);
            await Assert.ThrowsAsync<InvalidOperationException>(() => WebUiSession.SignInAsync(_app, "bob", Password));
        }

        [Fact]
        public async Task RejectsTakenUsername()
        {
            await WebUiSession.SeedLocalUserAsync(_app, "admin", Password, isAdmin: true);
            var bob = await WebUiSession.SeedLocalUserAsync(_app, "bob", Password);
            await WebUiSession.SeedLocalUserAsync(_app, "alice", Password);

            using var admin = await WebUiSession.SignInAsync(_app, "admin", Password);
            using var response = await admin.PostFormAsync("/Admin/Accounts", "Edit", Form(bob.Id, "ALICE"));

            Assert.Contains("already exists", await response.Content.ReadAsStringAsync());
            using var bobSession = await WebUiSession.SignInAsync(_app, "bob", Password);
        }

        [Fact]
        public async Task ChangesOnlyTheCaseOfTheUsername()
        {
            await WebUiSession.SeedLocalUserAsync(_app, "admin", Password, isAdmin: true);
            var bob = await WebUiSession.SeedLocalUserAsync(_app, "bob", Password);

            using var admin = await WebUiSession.SignInAsync(_app, "admin", Password);
            using var response = await admin.PostFormAsync("/Admin/Accounts", "Edit", Form(bob.Id, "Bob"));

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        [Theory]
        [InlineData("", "", "Username is required.")]
        [InlineData("bob", "not-an-email", "Enter a valid email address.")]
        public async Task RejectsInvalidValues(string username, string email, string message)
        {
            await WebUiSession.SeedLocalUserAsync(_app, "admin", Password, isAdmin: true);
            var bob = await WebUiSession.SeedLocalUserAsync(_app, "bob", Password);

            using var admin = await WebUiSession.SignInAsync(_app, "admin", Password);
            using var response = await admin.PostFormAsync("/Admin/Accounts", "Edit", Form(bob.Id, username, email: email));

            Assert.Contains(message, await response.Content.ReadAsStringAsync());
        }
    }

    public class ResetPassword : FactsBase
    {
        public ResetPassword(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public async Task SetsNewPasswordAndEndsLockout()
        {
            await WebUiSession.SeedLocalUserAsync(_app, "admin", Password, isAdmin: true);
            var bob = await WebUiSession.SeedLocalUserAsync(_app, "bob", Password);
            using (var scope = _app.Services.CreateScope())
            {
                var users = scope.ServiceProvider.GetRequiredService<IUserService>();
                for (var i = 0; i < 5; i++)
                {
                    await users.RecordFailedLoginAsync(bob.Id, CancellationToken.None);
                }
            }

            using var admin = await WebUiSession.SignInAsync(_app, "admin", Password);
            Assert.Contains("handler=ResetPassword", await admin.GetStringAsync("/Admin/Accounts"));

            using var response = await admin.PostFormAsync("/Admin/Accounts", "ResetPassword", new Dictionary<string, string>
            {
                { "userId", bob.Id.ToString() },
                { "newPassword", NewPassword },
            });
            var body = await response.Content.ReadAsStringAsync();
            Assert.Contains("reset successfully", body);
            // The create form's required fields aren't part of this post and must not complain.
            Assert.DoesNotContain(">Username is required.<", body);
            Assert.DoesNotContain(">Password is required.<", body);

            using var bobSession = await WebUiSession.SignInAsync(_app, "bob", NewPassword);
        }

        [Fact]
        public async Task RejectsShortPassword()
        {
            await WebUiSession.SeedLocalUserAsync(_app, "admin", Password, isAdmin: true);
            var bob = await WebUiSession.SeedLocalUserAsync(_app, "bob", Password);

            using var admin = await WebUiSession.SignInAsync(_app, "admin", Password);
            using var response = await admin.PostFormAsync("/Admin/Accounts", "ResetPassword", new Dictionary<string, string>
            {
                { "userId", bob.Id.ToString() },
                { "newPassword", "short" },
            });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("at least 12 characters", await response.Content.ReadAsStringAsync());
            using var bobSession = await WebUiSession.SignInAsync(_app, "bob", Password);
        }
    }
}
