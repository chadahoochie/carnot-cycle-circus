using CarnotCycleCircus.Core.Domain.Storage;
using Microsoft.Extensions.Hosting;

namespace CarnotCycleCircus.Core.Domain.Learning;

public class AutonomousSelfImprovementWorker : BackgroundService
{
    private readonly ISelfImprovementEngine _selfImprovementEngine;
    private readonly CarnotStorageOptions _options;

    public AutonomousSelfImprovementWorker(
        ISelfImprovementEngine selfImprovementEngine,
        CarnotStorageOptions options)
    {
        _selfImprovementEngine = selfImprovementEngine;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.AutoRunSelfImprovementOnStartup)
        {
            try
            {
                // Brief delay to allow domain services to initialize
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                await _selfImprovementEngine.RunSelfImprovementCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // Worker resilience against startup transient faults
            }
        }

        var interval = TimeSpan.FromSeconds(Math.Max(10, _options.SelfImprovementIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);
                await _selfImprovementEngine.RunSelfImprovementCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // Continue loop on unexpected error to keep self-improvement worker running
            }
        }
    }
}
