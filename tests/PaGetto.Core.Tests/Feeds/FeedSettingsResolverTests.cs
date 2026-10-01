using System;
using System.Linq;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using PaGetto.Core.Feeds;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace PaGetto.Core.Tests.Feeds;

public class FeedSettingsResolverTests
{
    private readonly PaGettoOptions _globalOptions;
    private readonly FeedSettingsResolver _target;

    public FeedSettingsResolverTests()
    {
        _globalOptions = new PaGettoOptions
        {
            AllowPackageOverwrites = PackageOverwriteAllowed.False,
            PackageDeletionBehavior = PackageDeletionBehavior.Unlist,
            IsReadOnlyMode = false,
            MaxPackageSizeMiB = 8192,
            Retention = new RetentionOptions
            {
                MaxMajorVersions = 5,
                MaxMinorVersions = null,
                MaxPatchVersions = null,
                MaxPrereleaseVersions = null,
            },
        };

        var snapshot = new Mock<IOptionsSnapshot<PaGettoOptions>>();
        snapshot.Setup(s => s.Value).Returns(() => _globalOptions);

        _target = new FeedSettingsResolver(snapshot.Object);
    }

    private static Feed DefaultFeed() => new Feed
    {
        Id = Guid.Empty,
        Slug = Feed.DefaultSlug,
        Name = "Default",
    };

    public class GetMaxPackageSizeMiB : FeedSettingsResolverTests
    {
        [Fact]
        public void ReturnsGlobalWhenFeedHasNoOverride()
        {
            Assert.Equal(8192u, _target.GetMaxPackageSizeMiB(DefaultFeed()));
        }

        [Fact]
        public void ReturnsFeedOverride()
        {
            var feed = DefaultFeed();
            feed.MaxPackageSizeMiB = 500;

            Assert.Equal(500u, _target.GetMaxPackageSizeMiB(feed));
        }

        [Fact]
        public void ConvertsTheLegacyGiBSetting()
        {
            _globalOptions.MaxPackageSizeMiB = null;
#pragma warning disable CS0618 // The legacy setting is still honored.
            _globalOptions.MaxPackageSizeGiB = 2;
#pragma warning restore CS0618

            Assert.Equal(2048u, _target.GetMaxPackageSizeMiB(DefaultFeed()));
        }

        [Fact]
        public void DefaultsTo8GiB()
        {
            _globalOptions.MaxPackageSizeMiB = null;

            Assert.Equal(PaGettoOptions.DefaultMaxPackageSizeMiB, _target.GetMaxPackageSizeMiB(DefaultFeed()));
        }
    }

    public class GetAllowPackageOverwrites : FeedSettingsResolverTests
    {
        [Fact]
        public void ReturnsGlobalWhenFeedHasNoOverride()
        {
            _globalOptions.AllowPackageOverwrites = PackageOverwriteAllowed.False;
            var feed = DefaultFeed();

            var result = _target.GetAllowPackageOverwrites(feed);

            Assert.Equal(PackageOverwriteAllowed.False, result);
        }

        [Fact]
        public void ReturnsFeedOverrideWhenSet()
        {
            _globalOptions.AllowPackageOverwrites = PackageOverwriteAllowed.False;
            var feed = DefaultFeed();
            feed.AllowPackageOverwrites = PackageOverwriteAllowed.True;

            var result = _target.GetAllowPackageOverwrites(feed);

            Assert.Equal(PackageOverwriteAllowed.True, result);
        }

        [Fact]
        public void FeedOverrideWinsOverGlobal()
        {
            _globalOptions.AllowPackageOverwrites = PackageOverwriteAllowed.True;
            var feed = DefaultFeed();
            feed.AllowPackageOverwrites = PackageOverwriteAllowed.False;

            var result = _target.GetAllowPackageOverwrites(feed);

            Assert.Equal(PackageOverwriteAllowed.False, result);
        }

        [Fact]
        public void ReturnsGlobalWhenFeedIsNull()
        {
            _globalOptions.AllowPackageOverwrites = PackageOverwriteAllowed.PrereleaseOnly;

            var result = _target.GetAllowPackageOverwrites(null);

            Assert.Equal(PackageOverwriteAllowed.PrereleaseOnly, result);
        }
    }

    public class GetPackageDeletionBehavior : FeedSettingsResolverTests
    {
        [Fact]
        public void ReturnsGlobalWhenFeedHasNoOverride()
        {
            _globalOptions.PackageDeletionBehavior = PackageDeletionBehavior.Unlist;
            var feed = DefaultFeed();

            var result = _target.GetPackageDeletionBehavior(feed);

            Assert.Equal(PackageDeletionBehavior.Unlist, result);
        }

        [Fact]
        public void ReturnsFeedOverride()
        {
            _globalOptions.PackageDeletionBehavior = PackageDeletionBehavior.Unlist;
            var feed = DefaultFeed();
            feed.PackageDeletionBehavior = PackageDeletionBehavior.HardDelete;

            var result = _target.GetPackageDeletionBehavior(feed);

            Assert.Equal(PackageDeletionBehavior.HardDelete, result);
        }
    }

    public class GetIsReadOnlyMode : FeedSettingsResolverTests
    {
        [Fact]
        public void ReturnsGlobalWhenFeedHasNoOverride()
        {
            _globalOptions.IsReadOnlyMode = true;
            var feed = DefaultFeed();

            var result = _target.GetIsReadOnlyMode(feed);

            Assert.True(result);
        }

        [Fact]
        public void ReturnsFeedOverride()
        {
            _globalOptions.IsReadOnlyMode = false;
            var feed = DefaultFeed();
            feed.IsReadOnlyMode = true;

            var result = _target.GetIsReadOnlyMode(feed);

            Assert.True(result);
        }
    }

    public class GetRetentionOptions : FeedSettingsResolverTests
    {
        [Fact]
        public void ReturnsGlobalWhenFeedHasNoOverrides()
        {
            _globalOptions.Retention = new RetentionOptions { MaxMajorVersions = 10 };
            var feed = DefaultFeed();

            var result = _target.GetRetentionOptions(feed);

            Assert.Equal((uint?)10, result.MaxMajorVersions);
        }

        [Fact]
        public void FeedOverrideReplacesGlobalForOverriddenFields()
        {
            _globalOptions.Retention = new RetentionOptions
            {
                MaxMajorVersions = 10,
                MaxMinorVersions = 5,
            };
            var feed = DefaultFeed();
            feed.RetentionMaxMajorVersions = 3;

            var result = _target.GetRetentionOptions(feed);

            Assert.Equal((uint?)3, result.MaxMajorVersions);
            Assert.Equal((uint?)5, result.MaxMinorVersions);
        }

        [Fact]
        public void UsesGlobalDeletePrereleasesOfOlderMajorsWhenFeedHasNoOverride()
        {
            _globalOptions.Retention = new RetentionOptions { DeletePrereleasesOfOlderMajors = true };

            var result = _target.GetRetentionOptions(DefaultFeed());

            Assert.True(result.DeletePrereleasesOfOlderMajors);
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void FeedOverrideReplacesGlobalDeletePrereleasesOfOlderMajors(bool global, bool feedValue)
        {
            _globalOptions.Retention = new RetentionOptions { DeletePrereleasesOfOlderMajors = global };
            var feed = DefaultFeed();
            feed.RetentionDeletePrereleasesOfOlderMajors = feedValue;

            var result = _target.GetRetentionOptions(feed);

            Assert.Equal(feedValue, result.DeletePrereleasesOfOlderMajors);
        }

        [Fact]
        public void ReturnsDefaultsWhenNoGlobalOrFeedRetention()
        {
            _globalOptions.Retention = null;
            var feed = DefaultFeed();

            var result = _target.GetRetentionOptions(feed);

            Assert.Null(result.MaxMajorVersions);
        }
    }

    public class GetUpstreamListingCacheDuration : FeedSettingsResolverTests
    {
        [Fact]
        public void ReturnsGlobalWhenFeedHasNoOverride()
        {
            _globalOptions.UpstreamListingCacheSeconds = 120;

            var result = _target.GetUpstreamListingCacheDuration(DefaultFeed());

            Assert.Equal(TimeSpan.FromSeconds(120), result);
        }

        [Fact]
        public void FeedOverrideWinsOverGlobal()
        {
            _globalOptions.UpstreamListingCacheSeconds = 120;
            var feed = DefaultFeed();
            feed.UpstreamListingCacheSeconds = 30;

            var result = _target.GetUpstreamListingCacheDuration(feed);

            Assert.Equal(TimeSpan.FromSeconds(30), result);
        }

        [Fact]
        public void FeedOverrideOfZeroDisablesTheCache()
        {
            _globalOptions.UpstreamListingCacheSeconds = 120;
            var feed = DefaultFeed();
            feed.UpstreamListingCacheSeconds = 0;

            var result = _target.GetUpstreamListingCacheDuration(feed);

            Assert.Equal(TimeSpan.Zero, result);
        }

        [Fact]
        public void DefaultsToFiveMinutes()
        {
            var result = new PaGettoOptions().UpstreamListingCacheSeconds;

            Assert.Equal(300, result);
        }
    }

    public class GetMirrorOptions : FeedSettingsResolverTests
    {
        [Fact]
        public void ReturnsEmptyWhenFeedHasNoMirrors()
        {
            var result = _target.GetMirrorOptions(DefaultFeed());

            Assert.Empty(result);
        }

        [Fact]
        public void ReturnsEmptyWhenFeedIsNull()
        {
            var result = _target.GetMirrorOptions(null);

            Assert.Empty(result);
        }

        [Fact]
        public void SkipsDisabledMirrors()
        {
            var feed = DefaultFeed();
            feed.Mirrors.Add(new FeedMirror { Enabled = false, PackageSource = "https://api.nuget.org/v3/index.json" });

            var result = _target.GetMirrorOptions(feed);

            Assert.Empty(result);
        }

        [Fact]
        public void ReturnsFeedMirrorSettings()
        {
            var feed = DefaultFeed();
            feed.Mirrors.Add(new FeedMirror
            {
                Enabled = true,
                PackageSource = "https://api.nuget.org/v3/index.json",
                Legacy = false,
                DownloadTimeoutSeconds = 300,
            });

            var result = Assert.Single(_target.GetMirrorOptions(feed));

            Assert.True(result.Enabled);
            Assert.Equal(new Uri("https://api.nuget.org/v3/index.json"), result.PackageSource);
            Assert.False(result.Legacy);
            Assert.Equal(300, result.PackageDownloadTimeoutSeconds);
        }

        [Fact]
        public void ReturnsFeedMirrorWithBasicAuth()
        {
            var feed = DefaultFeed();
            feed.Mirrors.Add(new FeedMirror
            {
                Enabled = true,
                PackageSource = "https://example.com/v3/index.json",
                AuthType = MirrorAuthenticationType.Basic,
                AuthUsername = "user",
                AuthPassword = "pass",
            });

            var result = Assert.Single(_target.GetMirrorOptions(feed));

            Assert.NotNull(result.Authentication);
            Assert.Equal(MirrorAuthenticationType.Basic, result.Authentication.Type);
            Assert.Equal("user", result.Authentication.Username);
            Assert.Equal("pass", result.Authentication.Password);
        }

        [Fact]
        public void ReturnsMirrorsInSortOrder()
        {
            var feed = DefaultFeed();
            feed.Mirrors.Add(new FeedMirror { Enabled = true, SortOrder = 1, PackageSource = "https://vendor.test/v3/index.json" });
            feed.Mirrors.Add(new FeedMirror { Enabled = true, SortOrder = 0, PackageSource = "https://api.nuget.org/v3/index.json" });

            var result = _target.GetMirrorOptions(feed);

            Assert.Equal(
                new[] { "api.nuget.org", "vendor.test" },
                result.Select(m => m.PackageSource.Host));
        }
    }
}
