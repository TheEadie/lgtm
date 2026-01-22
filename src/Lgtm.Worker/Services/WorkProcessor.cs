using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Lgtm.Worker.Services;

public class WorkProcessor : IWorkProcessor
{
    private readonly ILogger<WorkProcessor> _logger;
    private readonly IOptions<WorkerOptions> _options;
    private int _executionCount;

    public WorkProcessor(ILogger<WorkProcessor> logger, IOptions<WorkerOptions> options)
    {
        _logger = logger;
        _options = options;
    }

    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        var count = Interlocked.Increment(ref _executionCount);
        _logger.LogInformation("Starting weather fetch. Execution count: {Count}", count);

        try
        {
            var result = await RunClaudeAsync(_options.Value.WeatherPrompt, cancellationToken);
            _logger.LogInformation("Weather: {Result}", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch weather from Claude");
        }
    }

    private static async Task<string> RunClaudeAsync(string prompt, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "claude",
                Arguments = "--print --allowedTools WebSearch,WebFetch -p -",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        await process.StandardInput.WriteAsync(prompt);
        process.StandardInput.Close();

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Claude CLI exited with code {process.ExitCode}: {error}");
        }

        return output.Trim();
    }
}
