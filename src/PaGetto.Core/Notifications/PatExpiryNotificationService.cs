using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Configuration;
using PaGetto.Core.Email;
using PaGetto.Core.Entities;
using PaGetto.Core.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PaGetto.Core.Notifications;

/// <summary>
/// Periodically scans personal access tokens and emails their owners as expiry approaches,
/// once per configured threshold (see <see cref="PatExpiryNotificationOptions"/>).
/// </summary>
public partial class PatExpiryNotificationService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly PatExpiryNotificationOptions _options;
    private readonly IPatExpiryEmailBuilder _emailBuilder;
    private readonly SystemTime _systemTime;
    private readonly ILogger<PatExpiryNotificationService> _logger;

    public PatExpiryNotificationService(
        IServiceProvider serviceProvider,
        IOptions<PatExpiryNotificationOptions> options,
        IPatExpiryEmailBuilder emailBuilder,
        SystemTime systemTime,
        ILogger<PatExpiryNotificationService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _emailBuilder = emailBuilder ?? throw new ArgumentNullException(nameof(emailBuilder));
        _systemTime = systemTime ?? throw new ArgumentNullException(nameof(systemTime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            LogNotificationsDisabled();
            return;
        }

        // Nothing to do when no email backend is configured: the sender resolves to the no-op
        // NullEmailSender, so scanning would only burn the per-token notification state on
        // messages that are never delivered.
        using (var scope = _serviceProvider.CreateScope())
        {
            if (scope.ServiceProvider.GetRequiredService<IEmailSender>() is NullEmailSender)
            {
                LogScannerNotRunningEmailNotConfigured();
                return;
            }
        }

        var interval = TimeSpan.FromHours(_options.ScanIntervalHours);


        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Never let a single scan failure kill the loop.
                LogScanFailed(ex, interval);
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Runs a single scan pass: finds tokens crossing a notification threshold, emails their
    /// owners, and records the threshold so the same warning is not sent twice.
    /// </summary>
    /// <returns>The number of notification emails sent.</returns>
    internal async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        LogScanStarting();
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IContext>();
        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        // Defensive: never load tokens or record notification state when the effective sender is
        // the no-op sender (email not configured). ExecuteAsync already guards this, but keep the
        // invariant local so no-op sends can never advance the per-token de-dup threshold.
        if (emailSender is NullEmailSender)
        {
            LogScanSkippedEmailNotConfigured();
            return 0;
        }

        var now = _systemTime.UtcNow;
        var thresholds = _options.EffectiveNotificationDays;
        var maxThreshold = thresholds.Max();

        // Widen the window by a day so tokens on the boundary are not missed due to time-of-day.
        var cutoff = now.AddDays(maxThreshold + 1);

        // Only consider tokens that have not expired yet: a token past its expiry cannot be
        // "expiring soon", and warning about it would be misleading (and would fire for every
        // historically-expired, never-revoked token the first time the scanner runs).
        var tokens = await context.PersonalAccessTokens
            .Include(t => t.User)
            .Where(t => !t.IsRevoked && t.ExpiresAtUtc >= now && t.ExpiresAtUtc <= cutoff)
            .ToListAsync(cancellationToken);

        var sent = 0;

        foreach (var token in tokens)
        {
            var daysUntil = DaysUntil(token.ExpiresAtUtc, now);
            var due = GetDueThreshold(daysUntil, thresholds);
            if (due is null)
                continue;

            // Only send if we have not already notified at this (or a nearer) threshold.
            if (token.ExpiryNotificationThresholdDays is int already && already <= due.Value)
                continue;

            if (token.User is null || !token.User.IsEnabled)
                continue;

            if (string.IsNullOrWhiteSpace(token.User.Email))
            {
                LogUserHasNoEmail(token.Id, token.UserId);
                continue;
            }

            try
            {
                var message = _emailBuilder.Build(token, daysUntil);
                await emailSender.SendAsync(message, cancellationToken);

                // Record the threshold immediately so a later failure in this scan does not
                // discard progress (which would re-send this warning on the next scan).
                token.ExpiryNotificationThresholdDays = due.Value;
                await context.SaveChangesAsync(cancellationToken);
                sent++;

                LogNotificationSent(token.Id, token.UserId, due.Value);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Isolate per-token failures (e.g. a bad recipient address or a transient
                // send error) so one token cannot starve the rest of the batch.
                LogNotificationSendFailed(ex, token.Id, token.UserId);
            }
        }

        LogScanCompleted(sent);
        return sent;
    }

    /// <summary>
    /// The whole number of days from <paramref name="now"/> to <paramref name="expiresAtUtc"/>,
    /// measured by calendar date (so the expiry day itself is <c>0</c>, and past dates are negative).
    /// </summary>
    internal static int DaysUntil(DateTime expiresAtUtc, DateTime now)
    {
        return (expiresAtUtc.Date - now.Date).Days;
    }

    /// <summary>
    /// The smallest threshold that <paramref name="daysUntil"/> has reached, or <c>null</c> when
    /// the token is still further out than every threshold.
    /// </summary>
    internal static int? GetDueThreshold(int daysUntil, IReadOnlyList<int> thresholds)
    {
        var reached = thresholds.Where(t => daysUntil <= t).ToList();
        return reached.Count == 0 ? null : reached.Min();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "PAT expiry notifications are disabled; scanner will not run.")]
    private partial void LogNotificationsDisabled();

    [LoggerMessage(Level = LogLevel.Information, Message = "Email is not configured; PAT expiry scanner will not run.")]
    private partial void LogScannerNotRunningEmailNotConfigured();

    [LoggerMessage(Level = LogLevel.Error, Message = "PAT expiry notification scan failed; will retry after {Interval}.")]
    private partial void LogScanFailed(Exception exception, TimeSpan interval);

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting PAT expiry notification scan.")]
    private partial void LogScanStarting();

    [LoggerMessage(Level = LogLevel.Information, Message = "Email is not configured; skipping PAT expiry scan.")]
    private partial void LogScanSkippedEmailNotConfigured();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Token {TokenId} for user {UserId} expires soon but the user has no email address; skipping notification.")]
    private partial void LogUserHasNoEmail(Guid tokenId, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sent expiry notification for token {TokenId} (user {UserId}) at the {Threshold}-day threshold.")]
    private partial void LogNotificationSent(Guid tokenId, Guid userId, int threshold);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to send expiry notification for token {TokenId} (user {UserId}); skipping.")]
    private partial void LogNotificationSendFailed(Exception exception, Guid tokenId, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "PAT expiry notification scan complete; {Sent} emails sent.")]
    private partial void LogScanCompleted(int sent);
}
