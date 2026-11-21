using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Versioning;
using PaGetto.Core.Configuration;

namespace PaGetto.Core.Updates;

/// <summary>
/// Remembers the latest PaGetto release, looked up by <see cref="ReleaseCheckService"/>, so the
/// admin pages can tell whether a newer release than the running one exists. Each replica keeps
/// its own copy.
/// </summary>
public partial class ReleaseChecker
{
    private readonly GitHubReleaseClient _client;
    private readonly IOptionsMonitor<UpdateCheckOptions> _options;
    private readonly ILogger<ReleaseChecker> _logger;

    private volatile ReleaseInfo _latestRelease;

    public ReleaseChecker(GitHubReleaseClient client, IOptionsMonitor<UpdateCheckOptions> options, ILogger<ReleaseChecker> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Looks up the latest release, unless the check is turned off. A failure, e.g. without
    /// network access, is only logged at debug level and keeps the release found before.
    /// </summary>
    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        if (!_options.CurrentValue.Enabled)
        {
            return;
        }

        try
        {
            var release = await _client.GetLatestReleaseAsync(cancellationToken);
            if (release != null)
            {
                _latestRelease = release;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            LogCheckFailed(e);
        }
    }

    /// <summary>
    /// The latest release if it is newer than the running version.
    /// </summary>
    /// <param name="runningVersion">The running version, e.g. <c>1.2.0</c>.</param>
    /// <returns>Null if there is no newer release, none was found yet, or the check is turned off.</returns>
    public ReleaseInfo GetNewerRelease(string runningVersion)
    {
        var latest = _latestRelease;
        if (!_options.CurrentValue.Enabled
            || latest == null
            || !NuGetVersion.TryParse(runningVersion, out var running))
        {
            return null;
        }

        return latest.Version > running ? latest : null;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Looking up the latest PaGetto release failed.")]
    private partial void LogCheckFailed(Exception exception);
}
