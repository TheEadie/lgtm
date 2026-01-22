using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Lgtm.Worker.Services;

public class WorkProcessor : IWorkProcessor
{
    private static readonly HashSet<string> ProtectedBranches = ["main", "develop", "master"];

    private static readonly Regex PrUrlRegex = new(
        @"^https://github\.com/(?<owner>[^/]+)/(?<repo>[^/]+)/pull/(?<number>\d+)",
        RegexOptions.Compiled);

    private readonly List<string> _pullRequestUrls;
    private readonly WorkerOptions _options;
    private int _executionCount;

    public WorkProcessor(List<string> pullRequestUrls, IOptions<WorkerOptions> options)
    {
        _pullRequestUrls = pullRequestUrls;
        _options = options.Value;
    }

    public async Task ProcessAsync(CancellationToken cancellationToken)
    {
        var startTime = DateTime.Now;
        var count = Interlocked.Increment(ref _executionCount);
        Console.WriteLine($"Starting execution {count}");

        if (_pullRequestUrls.Count == 0)
        {
            Console.WriteLine("No pull requests configured.");
            return;
        }

        foreach (var prUrl in _pullRequestUrls)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            Console.WriteLine($"\n--- Processing: {prUrl} ---");

            try
            {
                try
                {
                    var prInfo = ParsePrUrl(prUrl);
                    if (prInfo is null)
                    {
                        Console.WriteLine("Invalid PR URL, skipping");
                        continue;
                    }

                    var (owner, repoName, prNumber) = prInfo.Value;

                    // Ensure repo is cloned and checked out to the PR branch
                    var repoPath = await EnsureRepoCheckedOutAsync(owner, repoName, prNumber, cancellationToken);
                    if (repoPath is null)
                    {
                        Console.WriteLine("Failed to clone/checkout repository, skipping");
                        continue;
                    }

                    Console.WriteLine($"Working in: {repoPath}");

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

                    if (status.Mergeable == "CONFLICTING")
                    {
                        if (IsProtectedBranch(status.HeadRefName))
                        {
                            Console.WriteLine($"ERROR: Cannot rebase protected branch '{status.HeadRefName}', skipping");
                            continue;
                        }

                        Console.WriteLine($"PR has conflicts, invoking Claude to rebase {status.HeadRefName} on {status.BaseRefName}");
                        var conflictPrompt = BuildConflictResolutionPrompt(status.HeadRefName, status.BaseRefName);
                        await RunClaudeStreamingAsync(conflictPrompt, repoPath, cancellationToken);
                        continue;
                    }

                    if (status.Mergeable != "MERGEABLE" && status.Mergeable != "UNKNOWN")
                    {
                        Console.WriteLine($"Unknown mergeable state: {status.Mergeable}, skipping");
                        continue;
                    }

                    // Check for new review comments since the last commit
                    Console.WriteLine($"Checking for new review comments since {status.LatestCommitDate?.ToString("u") ?? "unknown"}...");
                    var newComments = await GetNewReviewCommentsAsync(owner, repoName, prNumber, status.LatestCommitDate, cancellationToken);

                    if (newComments.Count == 0)
                    {
                        Console.WriteLine("No new review comments to address");
                        continue;
                    }

                    Console.WriteLine($"Found {newComments.Count} new review comment(s), invoking Claude to address them");
                    var reviewPrompt = BuildReviewResolutionPrompt(status.HeadRefName, newComments);
                    await RunClaudeStreamingAsync(reviewPrompt, repoPath, cancellationToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to process {prUrl}: {ex.Message}");
                }
                finally
                {
                    Console.WriteLine(new string('-', 60) + "\n");
                }
            }
            catch
            {
                // Suppress exceptions from finally block
            }
        }

        var nextRunTime = startTime.AddMinutes(_options.IntervalMinutes);
        Console.WriteLine($"\nNext check scheduled for: {nextRunTime:yyyy-MM-dd HH:mm:ss}");
    }

    private async Task<string?> EnsureRepoCheckedOutAsync(string owner, string repoName, int prNumber, CancellationToken cancellationToken)
    {
        var workspaceDir = ExpandPath(_options.WorkspaceDirectory);

        // Create workspace directory if it doesn't exist
        if (!Directory.Exists(workspaceDir))
        {
            Console.WriteLine($"Creating workspace directory: {workspaceDir}");
            Directory.CreateDirectory(workspaceDir);
        }

        var repoPath = Path.Combine(workspaceDir, $"{owner}-{repoName}");

        // If directory doesn't exist, clone the repo first
        if (!Directory.Exists(repoPath))
        {
            Console.WriteLine($"Cloning repository {owner}/{repoName}...");

            using var cloneProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "gh",
                    Arguments = $"repo clone {owner}/{repoName} {repoPath}",
                    WorkingDirectory = workspaceDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            cloneProcess.Start();
            await cloneProcess.StandardOutput.ReadToEndAsync(cancellationToken);
            var cloneError = await cloneProcess.StandardError.ReadToEndAsync(cancellationToken);
            await cloneProcess.WaitForExitAsync(cancellationToken);

            if (cloneProcess.ExitCode != 0)
            {
                Console.WriteLine($"Failed to clone repository: {cloneError}");
                return null;
            }

            Console.WriteLine($"Repository cloned successfully");
        }
        else
        {
            Console.WriteLine($"Reusing existing clone at {repoPath}");

            // Fetch latest changes from remote
            Console.WriteLine($"Fetching latest changes...");
            using var fetchProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "fetch --all --prune",
                    WorkingDirectory = repoPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            fetchProcess.Start();
            await fetchProcess.StandardOutput.ReadToEndAsync(cancellationToken);
            await fetchProcess.StandardError.ReadToEndAsync(cancellationToken);
            await fetchProcess.WaitForExitAsync(cancellationToken);

            if (fetchProcess.ExitCode != 0)
            {
                Console.WriteLine($"Warning: Failed to fetch latest changes, continuing with existing state");
            }
        }

        // Checkout the PR branch
        Console.WriteLine($"Checking out PR #{prNumber}...");

        using var checkoutProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "gh",
                Arguments = $"pr checkout {prNumber}",
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        checkoutProcess.Start();
        await checkoutProcess.StandardOutput.ReadToEndAsync(cancellationToken);
        var checkoutError = await checkoutProcess.StandardError.ReadToEndAsync(cancellationToken);
        await checkoutProcess.WaitForExitAsync(cancellationToken);

        if (checkoutProcess.ExitCode != 0)
        {
            Console.WriteLine($"Failed to checkout PR: {checkoutError}");
            return null;
        }

        Console.WriteLine($"Successfully checked out PR branch");
        return repoPath;
    }

    private async Task RunClaudeStreamingAsync(string prompt, string workingDirectory, CancellationToken cancellationToken)
    {
        var expandedPath = ExpandPath(workingDirectory);

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

    private record PrStatus(string State, string Mergeable, string HeadRefName, string BaseRefName, DateTimeOffset? LatestCommitDate);

    private record PrReview(string Author, string State, string Body, DateTimeOffset SubmittedAt);

    private record ReviewComment(string Author, string Path, int? Line, string Body, DateTimeOffset CreatedAt);

    private static async Task<PrStatus?> GetPrStatusAsync(string owner, string repo, int prNumber, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "gh",
                Arguments = $"pr view {prNumber} --repo {owner}/{repo} --json state,mergeable,headRefName,baseRefName,commits",
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

            DateTimeOffset? latestCommitDate = null;
            if (root.TryGetProperty("commits", out var commits) && commits.GetArrayLength() > 0)
            {
                var lastCommit = commits[commits.GetArrayLength() - 1];
                if (lastCommit.TryGetProperty("committedDate", out var dateElement))
                {
                    latestCommitDate = DateTimeOffset.Parse(dateElement.GetString()!);
                }
            }

            return new PrStatus(
                root.GetProperty("state").GetString() ?? "",
                root.GetProperty("mergeable").GetString() ?? "",
                root.GetProperty("headRefName").GetString() ?? "",
                root.GetProperty("baseRefName").GetString() ?? "",
                latestCommitDate
            );
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Failed to parse PR status: {ex.Message}");
            return null;
        }
    }

    private static async Task<List<ReviewComment>> GetNewReviewCommentsAsync(
        string owner, string repo, int prNumber, DateTimeOffset? sinceDate, CancellationToken cancellationToken)
    {
        var comments = new List<ReviewComment>();

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "gh",
                Arguments = $"api repos/{owner}/{repo}/pulls/{prNumber}/comments --paginate",
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
            Console.WriteLine($"Failed to get PR comments: gh exited with code {process.ExitCode}");
            return comments;
        }

        try
        {
            using var doc = JsonDocument.Parse(output);
            foreach (var comment in doc.RootElement.EnumerateArray())
            {
                var createdAt = DateTimeOffset.Parse(comment.GetProperty("created_at").GetString()!);

                // Skip comments older than the latest commit
                if (sinceDate.HasValue && createdAt <= sinceDate.Value)
                    continue;

                var author = comment.TryGetProperty("user", out var user)
                    ? user.GetProperty("login").GetString() ?? "unknown"
                    : "unknown";

                var path = comment.TryGetProperty("path", out var pathElement)
                    ? pathElement.GetString() ?? ""
                    : "";

                int? line = comment.TryGetProperty("line", out var lineElement) && lineElement.ValueKind == JsonValueKind.Number
                    ? lineElement.GetInt32()
                    : null;

                var body = comment.GetProperty("body").GetString() ?? "";

                comments.Add(new ReviewComment(author, path, line, body, createdAt));
            }
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Failed to parse PR comments: {ex.Message}");
        }

        return comments;
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

    private static string BuildReviewResolutionPrompt(string headRefName, List<ReviewComment> comments)
    {
        var commentDescriptions = string.Join("\n\n", comments.Select(c =>
        {
            var location = string.IsNullOrEmpty(c.Path)
                ? "General comment"
                : c.Line.HasValue
                    ? $"File: {c.Path}, Line: {c.Line}"
                    : $"File: {c.Path}";
            return $"[{c.Author}] {location}\n{c.Body}";
        }));

        return $"""
            There are new review comments on the PR for branch '{headRefName}' that need to be addressed.

            Review comments:
            {commentDescriptions}

            Please:
            1. Read and understand each review comment
            2. Make the necessary code changes to address the feedback
            3. Commit your changes with a descriptive message referencing the review feedback
            4. Push your changes: git push origin {headRefName}

            IMPORTANT: You are working on branch '{headRefName}'. Do not force push - use a regular push.
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

}
