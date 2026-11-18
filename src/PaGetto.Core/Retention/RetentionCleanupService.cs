using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PaGetto.Core.Retention;

/// <summary>
/// Runs the <see cref="RetentionCleaner"/> a minute after startup and then every hour.
/// </summary>
public partial class RetentionCleanupService : BackgroundService
{
    private static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RetentionCleanupService> _logger;

    public RetentionCleanupService(IServiceScopeFactory scopeFactory, ILogger<RetentionCleanupService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = FirstRunDelay;
        while (true)
        {
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            delay = Interval;

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var removed = await scope.ServiceProvider.GetRequiredService<RetentionCleaner>().RunAsync(stoppingToken);
                if (removed > 0)
                {
                    LogCleanupCompleted(removed);
                }
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
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Retention cleanup removed {Count} package versions.")]
    private partial void LogCleanupCompleted(int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "Retention cleanup failed; will retry after {Interval}.")]
    private partial void LogCleanupFailed(Exception exception, TimeSpan interval);
}
