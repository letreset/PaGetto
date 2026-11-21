using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace PaGetto.Core.Updates;

/// <summary>
/// Looks up the latest PaGetto release at startup and then once a day, through
/// <see cref="ReleaseChecker"/>, which does nothing while <c>UpdateCheck:Enabled</c> is false.
/// </summary>
public class ReleaseCheckService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    private readonly ReleaseChecker _checker;

    public ReleaseCheckService(ReleaseChecker checker)
    {
        _checker = checker ?? throw new ArgumentNullException(nameof(checker));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (true)
            {
                // Never throws but for the cancellation: a failed check is only logged.
                await _checker.CheckAsync(stoppingToken);
                await Task.Delay(Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
