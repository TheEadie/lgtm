using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lgtm.Worker.Services;

public class ScheduledWorkerService : BackgroundService
{
    private readonly IWorkProcessor _workProcessor;
    private readonly ILogger<ScheduledWorkerService> _logger;
    private readonly TimeSpan _interval;

    public ScheduledWorkerService(
        IWorkProcessor workProcessor,
        IOptions<WorkerOptions> options,
        ILogger<ScheduledWorkerService> logger)
    {
        _workProcessor = workProcessor;
        _logger = logger;
        _interval = TimeSpan.FromMinutes(options.Value.IntervalMinutes);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Scheduled worker service starting. Interval: {Interval}", _interval);

        // Execute immediately on startup
        await ExecuteWorkAsync(stoppingToken);

        // Then use PeriodicTimer for subsequent executions
        using var timer = new PeriodicTimer(_interval);

        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ExecuteWorkAsync(stoppingToken);
        }
    }

    private async Task ExecuteWorkAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _workProcessor.ProcessAsync(stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error occurred executing work");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Scheduled worker service stopping");
        await base.StopAsync(cancellationToken);
    }
}
