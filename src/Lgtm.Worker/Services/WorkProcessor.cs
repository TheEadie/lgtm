using System.Diagnostics;
using System.Text.Json;

namespace Lgtm.Worker.Services;

public class WorkProcessor : IWorkProcessor
{
    private const string Prompt = "Get the current weather in Cambridge, UK and provide a brief summary.";

    private readonly List<RepositoryConfig> _repositories;
    private int _executionCount;

    public WorkProcessor(List<RepositoryConfig> repositories)
    {
        _repositories = repositories;
    }

    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        var count = Interlocked.Increment(ref _executionCount);
        Console.WriteLine($"Starting execution {count}");

        if (_repositories.Count == 0)
        {
            Console.WriteLine("No repositories configured.");
            return;
        }

        foreach (var repo in _repositories)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            Console.WriteLine($"\n--- Processing: {repo.Path} ---");
            Console.WriteLine($"PR: {repo.PullRequestUrl}");

            try
            {
                await RunClaudeStreamingAsync(Prompt, repo.Path, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to process {repo.Path}: {ex.Message}");
            }
        }
    }

    private async Task RunClaudeStreamingAsync(string prompt, string workingDirectory, CancellationToken cancellationToken)
    {
        var expandedPath = ExpandPath(workingDirectory);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "claude",
                Arguments = "--verbose --output-format stream-json -p -",
                WorkingDirectory = expandedPath,
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

            if (type == "assistant")
            {
                ProcessAssistantMessage(root);
            }
        }
        catch (JsonException)
        {
            // Ignore malformed JSON
        }
    }

    private static string ExpandPath(string path)
    {
        if (path.StartsWith('~'))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, path[1..].TrimStart('/'));
        }
        return path;
    }

    private static void ProcessAssistantMessage(JsonElement root)
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
                    Console.WriteLine($"[Thinking] {thinking}");
                    break;
                case "text":
                    var text = block.GetProperty("text").GetString();
                    Console.WriteLine(text);
                    break;
                case "tool_use":
                    var toolName = block.GetProperty("name").GetString();
                    Console.WriteLine($"[Tool] {toolName}");
                    break;
            }
        }
    }

}
