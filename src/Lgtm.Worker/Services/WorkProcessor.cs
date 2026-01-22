using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lgtm.Worker.Services;

public class WorkProcessor : IWorkProcessor
{
    private static readonly HashSet<string> ProtectedBranches = ["main", "develop", "master"];

    private static readonly Regex PrUrlRegex = new(
        @"^https://github\.com/(?<owner>[^/]+)/(?<repo>[^/]+)/pull/(?<number>\d+)",
        RegexOptions.Compiled);

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
                var prInfo = ParsePrUrl(repo.PullRequestUrl);
                if (prInfo is null)
                {
                    Console.WriteLine("Invalid or missing PR URL, skipping status check");
                    continue;
                }

                var (owner, repoName, prNumber) = prInfo.Value;
                var status = await GetPrStatusAsync(owner, repoName, prNumber, cancellationToken);

                if (status is null)
                {
                    Console.WriteLine("Could not retrieve PR status, skipping");
                    continue;
                }

                Console.WriteLine($"PR State: {status.State}, Mergeable: {status.Mergeable}");
                Console.WriteLine($"Branch: {status.HeadRefName} -> {status.BaseRefName}");

                if (status.State == "MERGED")
                {
                    Console.WriteLine("PR already merged, skipping");
                    continue;
                }

                if (status.Mergeable == "MERGEABLE")
                {
                    Console.WriteLine("PR has no conflicts, skipping");
                    continue;
                }

                if (status.Mergeable == "CONFLICTING")
                {
                    if (IsProtectedBranch(status.HeadRefName))
                    {
                        Console.WriteLine($"ERROR: Cannot rebase protected branch '{status.HeadRefName}', skipping");
                        continue;
                    }

                    Console.WriteLine($"PR has conflicts, invoking Claude to rebase {status.HeadRefName} on {status.BaseRefName}");
                    var prompt = BuildConflictResolutionPrompt(status.HeadRefName, status.BaseRefName);
                    await RunClaudeStreamingAsync(prompt, repo.Path, cancellationToken);
                }
                else
                {
                    Console.WriteLine($"Unknown mergeable state: {status.Mergeable}, skipping");
                }
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

    private static (string owner, string repo, int prNumber)? ParsePrUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var match = PrUrlRegex.Match(url);
        if (!match.Success)
            return null;

        var owner = match.Groups["owner"].Value;
        var repo = match.Groups["repo"].Value;
        var number = int.Parse(match.Groups["number"].Value);

        return (owner, repo, number);
    }

    private static bool IsProtectedBranch(string branchName)
    {
        return ProtectedBranches.Contains(branchName.ToLowerInvariant());
    }

    private record PrStatus(string State, string Mergeable, string HeadRefName, string BaseRefName);

    private static async Task<PrStatus?> GetPrStatusAsync(string owner, string repo, int prNumber, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "gh",
                Arguments = $"pr view {prNumber} --repo {owner}/{repo} --json state,mergeable,headRefName,baseRefName",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            Console.WriteLine($"Failed to get PR status: gh exited with code {process.ExitCode}");
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;

            return new PrStatus(
                root.GetProperty("state").GetString() ?? "",
                root.GetProperty("mergeable").GetString() ?? "",
                root.GetProperty("headRefName").GetString() ?? "",
                root.GetProperty("baseRefName").GetString() ?? ""
            );
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Failed to parse PR status: {ex.Message}");
            return null;
        }
    }

    private static string BuildConflictResolutionPrompt(string headRefName, string baseRefName)
    {
        return $"""
            The PR branch '{headRefName}' has merge conflicts with '{baseRefName}'.
            Please:
            1. Run: git fetch origin
            2. Run: git rebase origin/{baseRefName}
            3. Resolve any merge conflicts that arise
            4. After resolving, run: git rebase --continue
            5. Once complete, run: git push --force-with-lease origin {headRefName}

            IMPORTANT: You are working on branch '{headRefName}'. Never force push to main or develop.
            """;
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
