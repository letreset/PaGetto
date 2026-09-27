using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using PaGetto.Core.Configuration;
using PaGetto.Core.Entities;
using Microsoft.Extensions.Options;

namespace PaGetto.Core.Feeds;

public class FeedSettingsResolver : IFeedSettingsResolver
{
    private readonly IOptionsSnapshot<PaGettoOptions> _options;

    public FeedSettingsResolver(IOptionsSnapshot<PaGettoOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public PackageOverwriteAllowed GetAllowPackageOverwrites(Feed feed)
    {
        return feed?.AllowPackageOverwrites ?? _options.Value.AllowPackageOverwrites;
    }

    public PackageDeletionBehavior GetPackageDeletionBehavior(Feed feed)
    {
        return feed?.PackageDeletionBehavior ?? _options.Value.PackageDeletionBehavior;
    }

    public bool GetIsReadOnlyMode(Feed feed)
    {
        return feed?.IsReadOnlyMode ?? _options.Value.IsReadOnlyMode;
    }

    public uint GetMaxPackageSizeMiB(Feed feed)
    {
        return feed?.MaxPackageSizeMiB ?? _options.Value.EffectiveMaxPackageSizeMiB;
    }

    public RetentionOptions GetRetentionOptions(Feed feed)
    {
        var global = _options.Value.Retention ?? new RetentionOptions();

        if (feed == null)
            return global;

        return new RetentionOptions
        {
            MaxMajorVersions = feed.RetentionMaxMajorVersions.HasValue
                ? (uint?)feed.RetentionMaxMajorVersions.Value
                : global.MaxMajorVersions,
            MaxMinorVersions = feed.RetentionMaxMinorVersions.HasValue
                ? (uint?)feed.RetentionMaxMinorVersions.Value
                : global.MaxMinorVersions,
            MaxPatchVersions = feed.RetentionMaxPatchVersions.HasValue
                ? (uint?)feed.RetentionMaxPatchVersions.Value
                : global.MaxPatchVersions,
            MaxPrereleaseVersions = feed.RetentionMaxPrereleaseVersions.HasValue
                ? (uint?)feed.RetentionMaxPrereleaseVersions.Value
                : global.MaxPrereleaseVersions,
        };
    }

    public TimeSpan GetUpstreamListingCacheDuration(Feed feed)
    {
        var seconds = feed?.UpstreamListingCacheSeconds ?? _options.Value.UpstreamListingCacheSeconds;

        return seconds > 0 ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
    }

    public IReadOnlyList<MirrorOptions> GetMirrorOptions(Feed feed)
    {
        if (feed?.Mirrors == null)
            return [];

        return feed.Mirrors
            .Where(m => m.Enabled && !string.IsNullOrEmpty(m.PackageSource))
            .OrderBy(m => m.SortOrder)
            .Select(ToMirrorOptions)
            .ToList();
    }

    private static MirrorOptions ToMirrorOptions(FeedMirror mirror)
    {
        var options = new MirrorOptions
        {
            Enabled = true,
            PackageSource = new Uri(mirror.PackageSource),
            Legacy = mirror.Legacy,
            PackageDownloadTimeoutSeconds = mirror.DownloadTimeoutSeconds ?? 600,
        };

        if (mirror.AuthType.HasValue && mirror.AuthType.Value != MirrorAuthenticationType.None)
        {
            options.Authentication = new MirrorAuthenticationOptions
            {
                Type = mirror.AuthType.Value,
                Username = mirror.AuthUsername,
                Password = mirror.AuthPassword,
                Token = mirror.AuthToken,
                CustomHeaders = DeserializeCustomHeaders(mirror.AuthCustomHeaders),
            };
        }

        return options;
    }

    private static Dictionary<string, string> DeserializeCustomHeaders(string json)
    {
        if (string.IsNullOrEmpty(json))
            return [];

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
