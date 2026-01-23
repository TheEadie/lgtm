using System.Diagnostics;
using System.Text.Json;
using Lgtm.Worker.Models;
using Microsoft.Extensions.Options;

namespace Lgtm.Worker.Services;

/// <summary>
/// Provides operations for interacting with GitHub repositories and Pull Requests via gh CLI and git.
/// </summary>
public class GitHubClient : IGitHubClient
{
    private readonly WorkerOptions _options;

    public GitHubClient(IOptions<WorkerOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc/>
    public async Task<string?> EnsureRepoCheckedOutAsync(string owner, string repoName, int prNumber, CancellationToken cancellationToken)
    {
        var workspaceDir = PathUtilities.ExpandPath(_options.WorkspaceDirectory);

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

    /// <inheritdoc/>
    public async Task<PrStatus?> GetPrStatusAsync(string owner, string repo, int prNumber, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "gh",
                Arguments = $"pr view {prNumber} --repo {owner}/{repo} --json state,mergeable,headRefName,baseRefName,commits,isDraft",
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

            var isDraft = root.TryGetProperty("isDraft", out var isDraftElement) && isDraftElement.GetBoolean();

            return new PrStatus(
                root.GetProperty("state").GetString() ?? "",
                root.GetProperty("mergeable").GetString() ?? "",
                root.GetProperty("headRefName").GetString() ?? "",
                root.GetProperty("baseRefName").GetString() ?? "",
                latestCommitDate,
                isDraft
            );
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Failed to parse PR status: {ex.Message}");
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<List<ReviewComment>> GetNewReviewCommentsAsync(
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

    /// <inheritdoc/>
    public async Task<bool> ConvertToDraftAsync(string owner, string repo, int prNumber, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Converting PR #{prNumber} to draft...");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "gh",
                Arguments = $"pr ready {prNumber} --repo {owner}/{repo} --undo",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            Console.WriteLine($"Warning: Failed to convert PR to draft: {error}");
            return false;
        }

        Console.WriteLine("PR converted to draft");
        return true;
    }
}
