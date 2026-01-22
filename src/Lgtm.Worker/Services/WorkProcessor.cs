using Microsoft.Extensions.Logging;

namespace Lgtm.Worker.Services;

public class WorkProcessor : IWorkProcessor
{
    private readonly ILogger<WorkProcessor> _logger;
    private int _executionCount;

    public WorkProcessor(ILogger<WorkProcessor> logger)
    {
        _logger = logger;
    }

    public Task ProcessAsync(CancellationToken cancellationToken)
    {
        var count = Interlocked.Increment(ref _executionCount);

        _logger.LogInformation(
            "Work executed at {Time}. Execution count: {Count}",
            DateTimeOffset.Now,
            count);

        return Task.CompletedTask;
    }
}
