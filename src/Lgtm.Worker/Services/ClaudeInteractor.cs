using System.Diagnostics;
using System.Text.Json;

namespace Lgtm.Worker.Services;

/// <summary>
/// Provides operations for interacting with the Claude CLI.
/// </summary>
public class ClaudeInteractor : IClaudeInteractor
{
    /// <inheritdoc/>
    public async Task RunClaudeStreamingAsync(string prompt, string workingDirectory, CancellationToken cancellationToken)
    {
        var expandedPath = PathUtilities.ExpandPath(workingDirectory);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "claude",
                Arguments = """--allowedTools "Bash(git*)" Edit Read --verbose --output-format stream-json -p -""",
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

    private static void ProcessStreamEvent(string json)
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
                case "tool_result":
                    ProcessToolResult(root);
                    break;
            }
        }
        catch (JsonException)
        {
            // Ignore malformed JSON
        }
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
                    if (block.TryGetProperty("input", out var input))
                    {
                        switch (toolName)
                        {
                            case "Bash" when input.TryGetProperty("command", out var command):
                                Console.WriteLine($"[Bash] {command.GetString()}");
                                continue;
                            case "Edit" or "Read" when input.TryGetProperty("file_path", out var filePath):
                                Console.WriteLine($"[{toolName}] {filePath.GetString()}");
                                continue;
                        }
                    }
                    Console.WriteLine($"[Tool] {toolName}");
                    break;
            }
        }
    }

    private static void ProcessToolResult(JsonElement root)
    {
        if (!root.TryGetProperty("tool_result", out var toolResult))
            return;

        // Get tool name if available
        var toolName = toolResult.TryGetProperty("tool_name", out var nameElement)
            ? nameElement.GetString()
            : "Tool";

        // Get the result content
        if (!toolResult.TryGetProperty("content", out var content))
            return;

        foreach (var block in content.EnumerateArray())
        {
            var blockType = block.GetProperty("type").GetString();

            switch (blockType)
            {
                case "text":
                    var text = block.GetProperty("text").GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        Console.WriteLine($"[Result] {text}");
                    }
                    break;
            }
        }

        // Check if tool execution had an error
        if (toolResult.TryGetProperty("is_error", out var isErrorElement) &&
            isErrorElement.GetBoolean())
        {
            Console.WriteLine($"[Error] {toolName} execution failed");
        }
    }
}
