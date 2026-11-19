using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PaGetto.Core.MaliciousPackages;

/// <summary>
/// Runs the <see cref="MaliciousPackageUpdater"/> a minute after startup and then every minute;
/// the updater only refreshes an ecosystem when its copy is due. Nothing is refreshed while no
/// mirroring feed blocks malicious packages.
/// </summary>
public partial class MaliciousPackageRefreshService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MaliciousPackageRefreshService> _logger;

    public MaliciousPackageRefreshService(IServiceScopeFactory scopeFactory, ILogger<MaliciousPackageRefreshService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<MaliciousPackageUpdater>().RunAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                // Never let a single failure kill the loop.
                LogRefreshFailed(e);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Refreshing the malicious packages failed.")]
    private partial void LogRefreshFailed(Exception exception);
}
