using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core;
using PaGetto.Core.Authentication;
using PaGetto.Core.Configuration;
using PaGetto.Core.Content;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using PaGetto.Core.Feeds;
using PaGetto.Core.Indexing;
using PaGetto.Core.Search;
using PaGetto.Web.Audit;
using PaGetto.Web.Pages;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NuGet.Versioning;
using Xunit;

namespace PaGetto.Web.Tests.Pages;

public class PackageModelFacts
{
    private readonly Mock<IPackageContentService> _content;
    private readonly Mock<IPackageService> _packages;
    private readonly Mock<ISearchService> _search;
    private readonly Mock<IUrlGenerator> _url;
    private readonly Mock<IFeedContext> _feedContext;
    private readonly Mock<IFeedSettingsResolver> _feedSettings = new();
    private readonly Mock<ILogger<WebAuditLog>> _auditLogger = new();
    private readonly Mock<SystemTime> _time = new();
    private static readonly DateTime _now = new(2020, 1, 11, 0, 0, 0, DateTimeKind.Utc);
    private readonly PackageModel _target;

    private readonly CancellationToken _cancellation = CancellationToken.None;
    private static readonly Guid _defaultFeedId = Guid.Empty;
    private const string DefaultFeedSlug = "default";

    public PackageModelFacts()
    {
        _content = new Mock<IPackageContentService>();
        _packages = new Mock<IPackageService>();
        _search = new Mock<ISearchService>();
        _url = new Mock<IUrlGenerator>();
        _feedContext = new Mock<IFeedContext>();

        var defaultFeed = new Feed { Id = _defaultFeedId, Slug = DefaultFeedSlug };
        _feedContext.Setup(f => f.CurrentFeed).Returns(defaultFeed);
        _time.Setup(t => t.UtcNow).Returns(_now);

        var permissions = new Mock<IPermissionService>();
        var deletionService = new Mock<IPackageDeletionService>();

        var authOptions = new Mock<IOptionsSnapshot<NugetAuthenticationOptions>>();
        authOptions.Setup(o => o.Value).Returns(new NugetAuthenticationOptions());

        _target = new PackageModel(
            _packages.Object,
            _content.Object,
            _search.Object,
            _url.Object,
            _feedContext.Object,
            permissions.Object,
            deletionService.Object,
            _feedSettings.Object,
            new WebAuditLog(_auditLogger.Object),
            authOptions.Object,
            _time.Object);

        _search
            .Setup(s => s.FindDependentsAsync(It.IsAny<Guid>(), "testpackage", _cancellation))
            .ReturnsAsync(new DependentsResponse());
    }

    [Fact]
    public async Task ReturnsNotFound()
    {
        _packages
            .Setup(m => m.FindPackagesAsync(It.IsAny<Guid>(), "testpackage", _cancellation))
            .ReturnsAsync(new List<Package>());

        await _target.OnGetAsync("testpackage", "1.0.0", _cancellation);

        Assert.False(_target.Found);
        Assert.Null(_target.Package);
        Assert.Equal("testpackage", _target.PackageIdNotFound);
        Assert.Null(_target.DependencyGroups);
        Assert.Null(_target.Versions);
    }

    [Fact]
    public async Task ReturnsNotFoundIfAllUnlisted()
    {
        _packages
            .Setup(m => m.FindPackagesAsync(It.IsAny<Guid>(), "testpackage", _cancellation))
            .ReturnsAsync(new List<Package>
            {
                CreatePackage("1.0.0", listed: false),
            });

        await _target.OnGetAsync("testpackage", version: null, _cancellation);

        Assert.False(_target.Found);
        Assert.Null(_target.Package);
        Assert.Equal("testpackage", _target.PackageIdNotFound);
        Assert.Null(_target.DependencyGroups);
        Assert.Null(_target.Versions);
    }

    [Fact]
    public async Task ReturnsRequestedVersion()
    {
        _packages
            .Setup(m => m.FindPackagesAsync(It.IsAny<Guid>(), "testpackage", _cancellation))
            .ReturnsAsync(new List<Package>
            {
                CreatePackage("1.0.0"),
                CreatePackage("2.0.0"),
                CreatePackage("3.0.0"),
            });

        await _target.OnGetAsync("testpackage", "2.0.0", _cancellation);

        Assert.True(_target.Found);
        Assert.Equal("testpackage", _target.Package.Id);
        Assert.Equal("2.0.0", _target.Package.NormalizedVersionString);

        Assert.Equal(3, _target.Versions.Count);
        Assert.Equal("3.0.0", _target.Versions[0].Version.OriginalVersion);
        Assert.False(_target.Versions[0].Selected);
        Assert.Equal("2.0.0", _target.Versions[1].Version.OriginalVersion);
        Assert.True(_target.Versions[1].Selected);
        Assert.Equal("1.0.0", _target.Versions[2].Version.OriginalVersion);
        Assert.False(_target.Versions[2].Selected);
    }

    [Fact]
    public async Task ReturnsRequestedUnlistedVersion()
    {
        _packages
            .Setup(m => m.FindPackagesAsync(It.IsAny<Guid>(), "testpackage", _cancellation))
            .ReturnsAsync(new List<Package>
            {
                CreatePackage("1.0.0"),
                CreatePackage("2.0.0", listed: false),
                CreatePackage("3.0.0"),
            });

        await _target.OnGetAsync("testpackage", "2.0.0", _cancellation);

        Assert.True(_target.Found);
        Assert.Equal("testpackage", _target.Package.Id);
        Assert.Equal("2.0.0", _target.Package.NormalizedVersionString);

        Assert.Equal(2, _target.Versions.Count);
        Assert.Equal("3.0.0", _target.Versions[0].Version.OriginalVersion);
        Assert.False(_target.Versions[0].Selected);
        Assert.Equal("1.0.0", _target.Versions[1].Version.OriginalVersion);
        Assert.False(_target.Versions[1].Selected);
    }

    [Theory]
    [InlineData("4.0.0")]
    [InlineData("not-a-version")]
    public async Task ReportsMissingRequestedVersion(string version)
    {
        _packages
            .Setup(m => m.FindPackagesAsync(It.IsAny<Guid>(), "testpackage", _cancellation))
            .ReturnsAsync(new List<Package> { CreatePackage("1.0.0"), CreatePackage("2.0.0") });

        await _target.OnGetAsync("testpackage", version, _cancellation);

        Assert.False(_target.Found);
        Assert.Equal(version, _target.VersionNotFound);
        Assert.Equal("testpackage", _target.Package.Id);
    }

    [Fact]
    public async Task FallsBackToLatestListedVersion()
    {
        _packages
            .Setup(m => m.FindPackagesAsync(It.IsAny<Guid>(), "testpackage", _cancellation))
            .ReturnsAsync(new List<Package>
            {
                CreatePackage("1.0.0"),
                CreatePackage("2.0.0"),
                CreatePackage("3.0.0", listed: false),
            });

        await _target.OnGetAsync("testpackage", null, _cancellation);

        Assert.True(_target.Found);
        Assert.Equal("testpackage", _target.Package.Id);
        Assert.Equal("2.0.0", _target.Package.NormalizedVersionString);

        Assert.Equal(2, _target.Versions.Count);
        Assert.Equal("2.0.0", _target.Versions[0].Version.OriginalVersion);
        Assert.True(_target.Versions[0].Selected);
        Assert.Equal("1.0.0", _target.Versions[1].Version.OriginalVersion);
        Assert.False(_target.Versions[1].Selected);
    }

    [Theory]
    [InlineData(new[] { "test" }, /*expectDotnetTemplate: */ false, /*expectDotnetTool: */ false)]
    [InlineData(new[] { "template" }, /*expectDotnetTemplate: */ true, /*expectDotnetTool: */ false)]
    [InlineData(new[] { "dOtNeTtOoL" }, /*expectDotnetTemplate: */ false, /*expectDotnetTool: */ true)]

    [InlineData(new[] { "tEmPlAte", "dOtNeTtOoL" }, /*expectDotnetTemplate: */ true, /*expectDotnetTool: */ true)]
    public async Task HandlesPackageTypes(IEnumerable<string> packageTypes, bool expectDotnetTemplate, bool expectDotnetTool)
    {
        _packages
            .Setup(m => m.FindPackagesAsync(It.IsAny<Guid>(), "testpackage", _cancellation))
            .ReturnsAsync(new List<Package>
            {
                CreatePackage("1.0.0", packageTypes: packageTypes)
            });

        await _target.OnGetAsync("testpackage", "1.0.0", _cancellation);

        Assert.True(_target.Found);
        Assert.Equal(expectDotnetTemplate, _target.IsDotnetTemplate);
        Assert.Equal(expectDotnetTool, _target.IsDotnetTool);
    }

    [Fact]
    public async Task FindsDependentPackages()
    {
        _packages
            .Setup(m => m.FindPackagesAsync(It.IsAny<Guid>(), "testpackage", _cancellation))
            .ReturnsAsync(new List<Package>
            {
                CreatePackage("1.0.0")
            });

        _search
            .Setup(s => s.FindDependentsAsync(It.IsAny<Guid>(), "testpackage", _cancellation))
            .ReturnsAsync(new DependentsResponse
            {
                Data = new List<PackageDependent>
                {
                    new PackageDependent  { Id = "Used by 1" },
                    new PackageDependent  { Id = "Used by 2" },
                }
            });

        await _target.OnGetAsync("testpackage", "1.0.0", _cancellation);

        Assert.Equal(2, _target.UsedBy.Count);
        Assert.Equal("Used by 1", _target.UsedBy[0].Id);
        Assert.Equal("Used by 2", _target.UsedBy[1].Id);
    }

    [Fact]
    public async Task GroupsVersions()
    {
        _packages
            .Setup(m => m.FindPackagesAsync(It.IsAny<Guid>(), "testpackage", _cancellation))
            .ReturnsAsync(new List<Package>
            {
                CreatePackage("1.0.0", dependencies: new[]
                {
                    new PackageDependency
                    {
                        TargetFramework = "net5.0",
                        Id = "Dependency1",
                        VersionRange = "[1.0.0, )",
                    },
                    new PackageDependency
                    {
                        TargetFramework = "net4.8",
                        Id = "Dependency2",
                        VersionRange = "[2.0.0, )",
                    },
                    new PackageDependency
                    {
                        TargetFramework = "net5.0",
                        Id = "Dependency3",
                        VersionRange = "[3.0.0, )",
                    },
                })
            });

        await _target.OnGetAsync("testpackage", "1.0.0", _cancellation);

        Assert.True(_target.Found);
        Assert.Equal(2, _target.DependencyGroups.Count);
        Assert.Equal(".NET 5.0", _target.DependencyGroups[0].Name);
        Assert.Equal(".NET Framework 4.8", _target.DependencyGroups[1].Name);

        Assert.Equal(2, _target.DependencyGroups[0].Dependencies.Count);
        Assert.Single(_target.DependencyGroups[1].Dependencies);

        Assert.Equal("Dependency1", _target.DependencyGroups[0].Dependencies[0].PackageId);
        Assert.Equal("(>= 1.0.0)", _target.DependencyGroups[0].Dependencies[0].VersionSpec);

        Assert.Equal("Dependency3", _target.DependencyGroups[0].Dependencies[1].PackageId);
        Assert.Equal("(>= 3.0.0)", _target.DependencyGroups[0].Dependencies[1].VersionSpec);

        Assert.Equal("Dependency2", _target.DependencyGroups[1].Dependencies[0].PackageId);
        Assert.Equal("(>= 2.0.0)", _target.DependencyGroups[1].Dependencies[0].VersionSpec);
    }

    [Theory]
    [InlineData(null, "All Frameworks")]
    [InlineData("net5.0", ".NET 5.0")]
    [InlineData("netstandard2.1", ".NET Standard 2.1")]
    [InlineData("netcoreapp3.1", ".NET Core 3.1")]
    [InlineData("net4.8", ".NET Framework 4.8")]
    public async Task PrettifiesTargetFramework(string targetFramework, string expectedResult)
    {
        _packages
            .Setup(m => m.FindPackagesAsync(It.IsAny<Guid>(), "testpackage", _cancellation))
            .ReturnsAsync(new List<Package>
            {
                CreatePackage("1.0.0", dependencies: new[]
                {
                   new PackageDependency
                   {
                       TargetFramework = targetFramework,
                       Id = "DependencyPackage",
                       VersionRange = "[1.0.0, )",
                   }
                })
            });

        await _target.OnGetAsync("testpackage", "1.0.0", _cancellation);

        Assert.True(_target.Found);
        var group = Assert.Single(_target.DependencyGroups);
        Assert.Equal(expectedResult, group.Name);
    }

    [Fact]
    public async Task ShowsTheLowestFrameworkPerFamilyAsBadges()
    {
        _packages
            .Setup(m => m.FindPackagesAsync(It.IsAny<Guid>(), "testpackage", _cancellation))
            .ReturnsAsync(new List<Package>
            {
                CreatePackage("1.0.0", targetFrameworks: ["net6.0", "net8.0", "netstandard2.0", "net20"]),
            });

        await _target.OnGetAsync("testpackage", "1.0.0", _cancellation);

        Assert.Equal([".NET 6.0", ".NET Standard 2.0", ".NET Framework 2.0"], _target.FrameworkBadges);
        Assert.Equal([".NET 8.0", ".NET 6.0", ".NET Standard 2.0", ".NET Framework 2.0"], _target.Frameworks);
    }

    [Fact]
    public async Task HasNoFrameworksWhenThePackageHasNone()
    {
        var package = CreatePackage("1.0.0");
        package.TargetFrameworks = null;
        _packages
            .Setup(m => m.FindPackagesAsync(It.IsAny<Guid>(), "testpackage", _cancellation))
            .ReturnsAsync(new List<Package> { package });

        await _target.OnGetAsync("testpackage", "1.0.0", _cancellation);

        Assert.Empty(_target.FrameworkBadges);
        Assert.Empty(_target.Frameworks);
    }

    [Fact]
    public async Task ComputesTheDailyDownloadAverageFromTheEarliestLocalVersion()
    {
        _packages
            .Setup(m => m.FindPackagesAsync(It.IsAny<Guid>(), "testpackage", _cancellation))
            .ReturnsAsync(new List<Package>
            {
                CreatePackage("1.0.0", downloads: 30, published: _now.AddDays(-10)),
                CreatePackage("2.0.0", downloads: 20, published: _now.AddDays(-2)),
                // Upstream dates don't count: downloads are only tracked for stored versions.
                CreatePackage("0.1.0", local: false, published: _now.AddYears(-5)),
            });

        await _target.OnGetAsync("testpackage", "2.0.0", _cancellation);

        Assert.Equal(5, _target.DailyDownloadAverage);
    }

    [Fact]
    public async Task CountsAtLeastOneDayForTheDailyDownloadAverage()
    {
        _packages
            .Setup(m => m.FindPackagesAsync(It.IsAny<Guid>(), "testpackage", _cancellation))
            .ReturnsAsync(new List<Package> { CreatePackage("1.0.0", downloads: 7, published: _now.AddHours(-1)) });

        await _target.OnGetAsync("testpackage", "1.0.0", _cancellation);

        Assert.Equal(7, _target.DailyDownloadAverage);
    }

    [Fact]
    public async Task HasNoDailyDownloadAverageWithoutLocalVersions()
    {
        _packages
            .Setup(m => m.FindPackagesAsync(It.IsAny<Guid>(), "testpackage", _cancellation))
            .ReturnsAsync(new List<Package> { CreatePackage("1.0.0", local: false) });

        await _target.OnGetAsync("testpackage", "1.0.0", _cancellation);

        Assert.Null(_target.DailyDownloadAverage);
    }

    [Fact]
    public async Task MarksPrereleaseVersions()
    {
        _packages
            .Setup(m => m.FindPackagesAsync(It.IsAny<Guid>(), "testpackage", _cancellation))
            .ReturnsAsync(new List<Package> { CreatePackage("1.0.0"), CreatePackage("2.0.0-beta.1") });

        await _target.OnGetAsync("testpackage", "1.0.0", _cancellation);

        Assert.True(_target.HasPrereleaseVersions);
        Assert.Collection(
            _target.Versions,
            v => Assert.True(v.IsPrerelease),
            v => Assert.False(v.IsPrerelease));
    }

    [Fact]
    public async Task LinksTheLicenseExpressionAndShowsTheSize()
    {
        var package = CreatePackage("1.0.0");
        package.LicenseExpression = "MIT OR Apache-2.0";
        package.LicenseUrl = new Uri("https://licenses.nuget.org/MIT%20OR%20Apache-2.0");
        package.Size = 2_548_000;
        _packages
            .Setup(m => m.FindPackagesAsync(It.IsAny<Guid>(), "testpackage", _cancellation))
            .ReturnsAsync(new List<Package> { package });

        await _target.OnGetAsync("testpackage", "1.0.0", _cancellation);

        Assert.Equal("MIT OR Apache-2.0 license", _target.LicenseText);
        Assert.Equal("https://licenses.nuget.org/MIT%20OR%20Apache-2.0", _target.LicenseUrl);
        Assert.Equal("2.43 MB", _target.PackageSize);
    }

    [Fact]
    public async Task FallsBackToTheLicenseUrlWithoutSize()
    {
        var package = CreatePackage("1.0.0");
        package.LicenseUrl = new Uri("https://license.test/");
        _packages
            .Setup(m => m.FindPackagesAsync(It.IsAny<Guid>(), "testpackage", _cancellation))
            .ReturnsAsync(new List<Package> { package });

        await _target.OnGetAsync("testpackage", "1.0.0", _cancellation);

        Assert.Equal("License", _target.LicenseText);
        Assert.Equal("https://license.test/", _target.LicenseUrl);
        Assert.Null(_target.PackageSize);
    }

    [Theory]
    [InlineData(812, "812 B")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1073741824, "1 GB")]
    public async Task FormatsTheSizeWithTheInvariantCulture(long size, string expected)
    {
        var package = CreatePackage("1.0.0");
        package.Size = size;
        _packages
            .Setup(m => m.FindPackagesAsync(It.IsAny<Guid>(), "testpackage", _cancellation))
            .ReturnsAsync(new List<Package> { package });

        await _target.OnGetAsync("testpackage", "1.0.0", _cancellation);

        Assert.Equal(expected, _target.PackageSize);
    }

    [Fact]
    public async Task StatisticsIncludeUnlistedPackages()
    {
        var now = DateTime.Now;

        _packages
            .Setup(m => m.FindPackagesAsync(It.IsAny<Guid>(), "testpackage", _cancellation))
            .ReturnsAsync(new List<Package>
            {
                CreatePackage("1.0.0", downloads: 10, published: DateTime.Now.AddDays(-2)),
                CreatePackage("2.0.0", listed: false, downloads: 5, published: now),
            });

        await _target.OnGetAsync("testpackage", "1.0.0", _cancellation);

        Assert.True(_target.Found);
        Assert.Equal(15, _target.TotalDownloads);
        Assert.Equal(now, _target.LastUpdated);
    }

    [Fact]
    public async Task UrlMetadataIsEmptyStringWhenAbsent()
    {
        // The Package page guards link rendering with !string.IsNullOrEmpty(...) because
        // these properties return string.Empty (never null) when the metadata is absent.
        _packages
            .Setup(m => m.FindPackagesAsync(It.IsAny<Guid>(), "testpackage", _cancellation))
            .ReturnsAsync(new List<Package>
            {
                CreatePackage("1.0.0"),
            });

        await _target.OnGetAsync("testpackage", "1.0.0", _cancellation);

        Assert.Equal(string.Empty, _target.Package.ProjectUrlString);
        Assert.Equal(string.Empty, _target.Package.RepositoryUrlString);
        Assert.Equal(string.Empty, _target.LicenseUrl);
    }

    [Fact]
    public async Task RendersReadme()
    {
        using var readmeStream = new MemoryStream();
        using (var streamWriter = new StreamWriter(readmeStream, leaveOpen: true))
        {
            await streamWriter.WriteLineAsync("# My readme");
            await streamWriter.WriteLineAsync("Hello world!");
            await streamWriter.FlushAsync();
        }

        readmeStream.Position = 0;

        _packages
            .Setup(m => m.FindPackagesAsync(It.IsAny<Guid>(), "testpackage", _cancellation))
            .ReturnsAsync(new List<Package>
            {
                CreatePackage("1.0.0", hasReadme: true),
            });

        _content
            .Setup(c => c.GetPackageReadmeStreamOrNullAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                "testpackage",
                It.Is<NuGetVersion>(v => v.OriginalVersion == "1.0.0"),
                _cancellation))
            .ReturnsAsync(readmeStream);

        await _target.OnGetAsync("testpackage", "1.0.0", _cancellation);

        Assert.Equal(
            "<h1 id=\"my-readme\">My readme</h1>\n<p>Hello world!</p>\n",
            _target.Readme.Value);
    }

    [Fact]
    public async Task IncludesUnlistedVersionsStruckThroughForManagers()
    {
        _packages
            .Setup(m => m.FindPackagesAsync(It.IsAny<Guid>(), "testpackage", _cancellation))
            .ReturnsAsync(new List<Package>
            {
                CreatePackage("1.0.0"),
                CreatePackage("2.0.0", listed: false),
                CreatePackage("3.0.0"),
            });

        var permissions = new Mock<IPermissionService>();
        permissions.Setup(p => p.CanPullAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), _cancellation)).ReturnsAsync(true);
        permissions.Setup(p => p.CanDeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), _cancellation)).ReturnsAsync(true);

        var authOptions = new Mock<IOptionsSnapshot<NugetAuthenticationOptions>>();
        authOptions.Setup(o => o.Value).Returns(new NugetAuthenticationOptions { Mode = AuthenticationMode.Entra });

        var target = new PackageModel(
            _packages.Object, _content.Object, _search.Object, _url.Object,
            _feedContext.Object, permissions.Object, new Mock<IPackageDeletionService>().Object, _feedSettings.Object, new WebAuditLog(_auditLogger.Object), authOptions.Object, _time.Object);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) }, "TestAuth"));
        target.PageContext = new PageContext(new ActionContext(
            new DefaultHttpContext { User = principal }, new RouteData(), new PageActionDescriptor()));

        await target.OnGetAsync("testpackage", "1.0.0", _cancellation);

        Assert.True(target.CanDelete);
        Assert.Equal(3, target.Versions.Count);
        var unlisted = Assert.Single(target.Versions, v => !v.Listed);
        Assert.Equal("2.0.0", unlisted.Version.OriginalVersion);
    }

    [Fact]
    public async Task HidesManageActionsOnReadOnlyFeed()
    {
        _packages
            .Setup(m => m.FindPackagesAsync(It.IsAny<Guid>(), "testpackage", _cancellation))
            .ReturnsAsync(new List<Package> { CreatePackage("1.0.0"), CreatePackage("2.0.0", listed: false) });
        _feedSettings.Setup(s => s.GetIsReadOnlyMode(It.IsAny<Feed>())).Returns(true);
        var (target, _) = CreateManagerTarget();

        await target.OnGetAsync("testpackage", "1.0.0", _cancellation);

        Assert.True(target.CanDelete);
        Assert.True(target.IsReadOnly);
        Assert.False(target.CanManage);
    }

    [Theory]
    [InlineData("Unlist")]
    [InlineData("Relist")]
    [InlineData("Delete")]
    public async Task RefusesManageActionsOnReadOnlyFeed(string handler)
    {
        _feedSettings.Setup(s => s.GetIsReadOnlyMode(It.IsAny<Feed>())).Returns(true);
        var (target, deletion) = CreateManagerTarget();

        var result = handler switch
        {
            "Unlist" => await target.OnPostUnlistAsync("testpackage", "1.0.0", _cancellation),
            "Relist" => await target.OnPostRelistAsync("testpackage", "1.0.0", _cancellation),
            _ => await target.OnPostDeleteAsync("testpackage", "1.0.0", _cancellation),
        };

        Assert.Equal(403, Assert.IsType<StatusCodeResult>(result).StatusCode);
        Assert.Empty(deletion.Invocations);
    }

    [Theory]
    [InlineData("Unlist", true, LogLevel.Information, "package_unlist_succeeded")]
    [InlineData("Relist", true, LogLevel.Information, "package_relist_succeeded")]
    [InlineData("Delete", true, LogLevel.Information, "package_delete_succeeded")]
    [InlineData("Unlist", false, LogLevel.Warning, "package_unlist_not_found")]
    public async Task AuditsManageActions(string handler, bool found, LogLevel level, string eventName)
    {
        _auditLogger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        var (target, deletion) = CreateManagerTarget();
        var version = NuGetVersion.Parse("1.0.0");
        deletion.Setup(d => d.TryUnlistPackageAsync(_defaultFeedId, "testpackage", version, _cancellation)).ReturnsAsync(found);
        deletion.Setup(d => d.TryRelistPackageAsync(_defaultFeedId, "testpackage", version, _cancellation)).ReturnsAsync(found);
        deletion.Setup(d => d.TryHardDeletePackageAsync(_defaultFeedId, DefaultFeedSlug, "testpackage", version, _cancellation)).ReturnsAsync(found);

        _ = handler switch
        {
            "Unlist" => await target.OnPostUnlistAsync("testpackage", "1.0.0", _cancellation),
            "Relist" => await target.OnPostRelistAsync("testpackage", "1.0.0", _cancellation),
            _ => await target.OnPostDeleteAsync("testpackage", "1.0.0", _cancellation),
        };

        VerifyAudit(level, $"AUDIT {eventName} feed=default package_id=testpackage package_version=1.0.0 actor=");
    }

    [Fact]
    public async Task AuditsRefusedManageActionOnReadOnlyFeed()
    {
        _auditLogger.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        _feedSettings.Setup(s => s.GetIsReadOnlyMode(It.IsAny<Feed>())).Returns(true);
        var (target, _) = CreateManagerTarget();

        await target.OnPostDeleteAsync("testpackage", "1.0.0", _cancellation);

        VerifyAudit(LogLevel.Warning, "AUDIT package_delete_read_only feed=default package_id=testpackage package_version=1.0.0 actor=");
    }

    private void VerifyAudit(LogLevel level, string expectedPrefix)
    {
        _auditLogger.Verify(
            l => l.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString().StartsWith(expectedPrefix)),
                null,
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    /// <summary>
    /// A page model for a signed-in user with pull and delete permission in Entra mode.
    /// </summary>
    private (PackageModel Target, Mock<IPackageDeletionService> Deletion) CreateManagerTarget()
    {
        var permissions = new Mock<IPermissionService>();
        permissions.Setup(p => p.CanPullAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), _cancellation)).ReturnsAsync(true);
        permissions.Setup(p => p.CanDeleteAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), _cancellation)).ReturnsAsync(true);

        var authOptions = new Mock<IOptionsSnapshot<NugetAuthenticationOptions>>();
        authOptions.Setup(o => o.Value).Returns(new NugetAuthenticationOptions { Mode = AuthenticationMode.Entra });

        var deletion = new Mock<IPackageDeletionService>();
        var target = new PackageModel(
            _packages.Object, _content.Object, _search.Object, _url.Object,
            _feedContext.Object, permissions.Object, deletion.Object, _feedSettings.Object, new WebAuditLog(_auditLogger.Object), authOptions.Object, _time.Object);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) }, "TestAuth"));
        target.PageContext = new PageContext(new ActionContext(
            new DefaultHttpContext { User = principal }, new RouteData(), new PageActionDescriptor()));

        return (target, deletion);
    }

    [Fact]
    public async Task MarksMirrorOnlyVersionsAndHidesTheirManageActions()
    {
        _packages
            .Setup(m => m.FindPackagesAsync(It.IsAny<Guid>(), "testpackage", _cancellation))
            .ReturnsAsync(new List<Package>
            {
                CreatePackage("1.0.0"),
                CreatePackage("2.0.0", local: false, published: new DateTime(2024, 5, 1)),
                CreatePackage("3.0.0-beta", listed: false, local: false, published: new DateTime(1900, 1, 1)),
            });
        var (target, _) = CreateManagerTarget();

        await target.OnGetAsync("testpackage", "2.0.0", _cancellation);

        Assert.True(target.CanManage);
        Assert.False(target.IsStoredLocally);
        Assert.Collection(
            target.Versions,
            v => Assert.False(v.IsLocal),
            v => Assert.True(v.IsLocal));
        Assert.DoesNotContain(target.Versions, v => v.Version.OriginalVersion == "3.0.0-beta");
    }

    [Fact]
    public async Task HidesMissingUpstreamDates()
    {
        _packages
            .Setup(m => m.FindPackagesAsync(It.IsAny<Guid>(), "testpackage", _cancellation))
            .ReturnsAsync(new List<Package>
            {
                CreatePackage("1.0.0"),
                CreatePackage("2.0.0", local: false, published: new DateTime(1900, 1, 1)),
            });

        await _target.OnGetAsync("testpackage", "1.0.0", _cancellation);

        Assert.Null(Assert.Single(_target.Versions, v => !v.IsLocal).LastUpdated);
    }

    private int _nextKey = 1;

    private Package CreatePackage(
        string version,
        long downloads = 0,
        bool hasReadme = false,
        bool listed = true,
        DateTime? published = null,
        IEnumerable<PackageDependency> dependencies = null,
        IEnumerable<string> packageTypes = null,
        bool local = true,
        IEnumerable<string> targetFrameworks = null)
    {
        published ??= DateTime.Now;
        dependencies ??= Array.Empty<PackageDependency>();
        packageTypes ??= Array.Empty<string>();
        targetFrameworks ??= Array.Empty<string>();

        return new Package
        {
            // Packages read from the database have a key, mirror-only ones don't.
            Key = local ? _nextKey++ : 0,
            Id = "testpackage",
            Downloads = downloads,
            HasReadme = hasReadme,
            Listed = listed,
            NormalizedVersionString = version,
            Published = published.Value,

            Dependencies = dependencies.ToList(),
            PackageTypes = packageTypes
                .Select(name => new PackageType { Name = name })
                .ToList(),
            TargetFrameworks = targetFrameworks
                .Select(moniker => new TargetFramework { Moniker = moniker })
                .ToList(),
        };
    }
}
