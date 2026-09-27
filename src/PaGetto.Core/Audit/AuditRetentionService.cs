using System;
using System.Threading;
using System.Threading.Tasks;
using PaGetto.Core.Configuration;
using PaGetto.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PaGetto.Core.Audit;

/// <summary>
/// Deletes audit events older than <see cref="AuditOptions.RetentionDays"/>, at startup and then once a day.
/// </summary>
public partial class AuditRetentionService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    private readonly IServiceProvider _serviceProvider;
    private readonly AuditOptions _options;
    private readonly SystemTime _systemTime;
    private readonly ILogger<AuditRetentionService> _logger;

    public AuditRetentionService(
        IServiceProvider serviceProvider,
        IOptions<AuditOptions> options,
        SystemTime systemTime,
        ILogger<AuditRetentionService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _systemTime = systemTime ?? throw new ArgumentNullException(nameof(systemTime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.RetentionDays == 0)
        {
            LogRetentionDisabled();
            return;
        }

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
                // Never let a single failure kill the loop.
                LogCleanupFailed(ex, Interval);
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <returns>The number of deleted events.</returns>
    internal async Task<int> RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var auditEvents = scope.ServiceProvider.GetRequiredService<IAuditEventService>();

        var cutoff = _systemTime.UtcNow.AddDays(-_options.RetentionDays);
        var deleted = await auditEvents.DeleteOlderThanAsync(cutoff, cancellationToken);

        LogCleanupCompleted(deleted, cutoff);
        return deleted;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Audit retention is off; audit events are kept forever.")]
    private partial void LogRetentionDisabled();

    [LoggerMessage(Level = LogLevel.Error, Message = "Audit event cleanup failed; will retry after {Interval}.")]
    private partial void LogCleanupFailed(Exception exception, TimeSpan interval);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {Count} audit events older than {Cutoff:O}.")]
    private partial void LogCleanupCompleted(int count, DateTime cutoff);
}
