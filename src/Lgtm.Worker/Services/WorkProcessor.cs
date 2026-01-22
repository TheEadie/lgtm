using System.Diagnostics;
using System.Text.Json;
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
            await RunClaudeStreamingAsync(_options.Value.WeatherPrompt, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch weather from Claude");
        }
    }

    private async Task RunClaudeStreamingAsync(string prompt, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "claude",
                Arguments = "--verbose --output-format stream-json --allowedTools WebSearch,WebFetch -p -",
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

        string? line;
        while ((line = await process.StandardOutput.ReadLineAsync(cancellationToken)) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            ProcessStreamEvent(line);
        }

        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Claude CLI exited with code {process.ExitCode}: {error}");
        }
    }

    private void ProcessStreamEvent(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var type = root.GetProperty("type").GetString();

            switch (type)
            {
                case "assistant":
                    ProcessAssistantMessage(root);
                    break;
                case "result":
                    ProcessResultMessage(root);
                    break;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse stream event: {Json}", json);
        }
    }

    private void ProcessAssistantMessage(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var message))
            return;

        if (!message.TryGetProperty("content", out var content))
            return;

        foreach (var block in content.EnumerateArray())
        {
            var blockType = block.GetProperty("type").GetString();

            switch (blockType)
            {
                case "thinking":
                    var thinking = block.GetProperty("thinking").GetString();
                    _logger.LogDebug("Thinking: {Thinking}", thinking);
                    break;
                case "text":
                    var text = block.GetProperty("text").GetString();
                    _logger.LogInformation("Claude: {Text}", text);
                    break;
                case "tool_use":
                    var toolName = block.GetProperty("name").GetString();
                    _logger.LogInformation("Using tool: {Tool}", toolName);
                    break;
            }
        }
    }

    private void ProcessResultMessage(JsonElement root)
    {
        if (root.TryGetProperty("result", out var result))
        {
            _logger.LogInformation("Final result: {Result}", result.GetString());
        }
    }
}
