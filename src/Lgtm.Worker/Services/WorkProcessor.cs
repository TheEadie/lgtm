using Lgtm.Worker.Models;
using Microsoft.Extensions.Options;

namespace Lgtm.Worker.Services;

public class WorkProcessor : IWorkProcessor
{
    private readonly IGitHubClient _gitHubClient;
    private readonly IClaudeInteractor _claudeInteractor;
    private readonly IResolutionPromptBuilder _promptBuilder;
    private readonly IPrStateTracker _stateTracker;
    private readonly List<string> _pullRequestUrls;
    private readonly WorkerOptions _options;
    private int _executionCount;

    public WorkProcessor(
        IGitHubClient gitHubClient,
        IClaudeInteractor claudeInteractor,
        IResolutionPromptBuilder promptBuilder,
        IPrStateTracker stateTracker,
        List<string> pullRequestUrls,
        IOptions<WorkerOptions> options)
    {
        _gitHubClient = gitHubClient;
        _claudeInteractor = claudeInteractor;
        _promptBuilder = promptBuilder;
        _stateTracker = stateTracker;
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

        // Load persisted state
        await _stateTracker.LoadStateAsync(cancellationToken);
        var stateModified = false;

        foreach (var prUrl in _pullRequestUrls)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            Console.WriteLine($"\n--- Processing: {prUrl} ---");

            try
            {
                try
                {
                    var prInfo = PathUtilities.ParsePrUrl(prUrl);
                    if (prInfo is null)
                    {
                        Console.WriteLine("Invalid PR URL, skipping");
                        continue;
                    }

                    var (owner, repoName, prNumber) = prInfo;

                    // Get PR status first (uses GitHub API, no checkout needed)
                    var status = await _gitHubClient.GetPrStatusAsync(owner, repoName, prNumber, cancellationToken);

                    if (status is null)
                    {
                        Console.WriteLine("Could not retrieve PR status, skipping");
                        continue;
                    }

                    Console.WriteLine($"PR State: {status.State}, Mergeable: {status.Mergeable}");
                    Console.WriteLine($"Branch: {status.HeadRefName} -> {status.BaseRefName}");
                    Console.WriteLine($"Head commit: {status.HeadCommitSha[..Math.Min(7, status.HeadCommitSha.Length)]}");

                    if (status.State == "MERGED")
                    {
                        if (_stateTracker.IsFirstSeenAsMerged(prUrl))
                        {
                            Console.WriteLine("PR has been merged!");
                            _stateTracker.RecordMerge(prUrl);
                            stateModified = true;
                        }
                        else
                        {
                            Console.WriteLine("PR already merged, skipping");
                        }
                        continue;
                    }

                    if (status.IsDraft)
                    {
                        Console.WriteLine("PR is a draft (pending user review), skipping");
                        continue;
                    }

                    // Get the latest comment ID for fingerprinting
                    var latestCommentId = await _gitHubClient.GetLatestCommentIdAsync(owner, repoName, prNumber, cancellationToken);

                    // Build current state fingerprint
                    var fingerprint = _stateTracker.GetCurrentFingerprint(prUrl, status, latestCommentId);

                    // Determine if there's work to do
                    bool hasConflicts = status.Mergeable == "CONFLICTING";
                    bool shouldResolveConflicts = hasConflicts && _stateTracker.ShouldResolveConflicts(prUrl, fingerprint);
                    bool shouldAddressReviews = false;
                    List<ReviewComment>? newComments = null;

                    if (!hasConflicts)
                    {
                        if (status.Mergeable != "MERGEABLE" && status.Mergeable != "UNKNOWN")
                        {
                            Console.WriteLine($"Unknown mergeable state: {status.Mergeable}, skipping");
                            continue;
                        }

                        // Check if we should address reviews based on comment IDs
                        if (_stateTracker.ShouldAddressReviews(prUrl, latestCommentId))
                        {
                            // Get comments after the last addressed comment ID
                            var lastAddressedId = _stateTracker.GetLastAddressedCommentId(prUrl);
                            Console.WriteLine($"Checking for new review comments (after ID {lastAddressedId?.ToString() ?? "none"})...");
                            newComments = await _gitHubClient.GetReviewCommentsAfterIdAsync(owner, repoName, prNumber, lastAddressedId, cancellationToken);

                            if (newComments.Count > 0)
                            {
                                shouldAddressReviews = true;
                            }
                            else
                            {
                                Console.WriteLine("No new review comments to address");
                            }
                        }
                    }
                    else if (!shouldResolveConflicts)
                    {
                        // Has conflicts but state tracking says we already handled this state
                        continue;
                    }

                    if (!shouldResolveConflicts && !shouldAddressReviews)
                    {
                        Console.WriteLine("No work needed for this PR");
                        continue;
                    }

                    // We have work to do - now checkout the repo
                    if (shouldResolveConflicts)
                    {
                        if (PathUtilities.IsProtectedBranch(status.HeadRefName))
                        {
                            Console.WriteLine($"ERROR: Cannot rebase protected branch '{status.HeadRefName}', skipping");
                            continue;
                        }
                    }

                    var repoPath = await _gitHubClient.EnsureRepoCheckedOutAsync(owner, repoName, prNumber, cancellationToken);
                    if (repoPath is null)
                    {
                        Console.WriteLine("Failed to clone/checkout repository, skipping");
                        continue;
                    }

                    Console.WriteLine($"Working in: {repoPath}");

                    if (shouldResolveConflicts)
                    {
                        Console.WriteLine($"PR has conflicts, invoking Claude to rebase {status.HeadRefName} on {status.BaseRefName}");
                        var conflictPrompt = _promptBuilder.BuildConflictResolutionPrompt(status.HeadRefName, status.BaseRefName);
                        await _claudeInteractor.RunClaudeStreamingAsync(conflictPrompt, repoPath, cancellationToken);

                        // Record successful conflict resolution
                        _stateTracker.RecordConflictResolution(prUrl, fingerprint);
                        stateModified = true;
                        continue;
                    }

                    Console.WriteLine($"Found {newComments!.Count} new review comment(s), invoking Claude to address them");
                    var reviewPrompt = _promptBuilder.BuildReviewResolutionPrompt(status.HeadRefName, newComments);
                    await _claudeInteractor.RunClaudeStreamingAsync(reviewPrompt, repoPath, cancellationToken);

                    // Convert PR to draft so user can review changes before notifying reviewers
                    await _gitHubClient.ConvertToDraftAsync(owner, repoName, prNumber, cancellationToken);

                    // Record successful review resolution with the max comment ID we addressed
                    var maxCommentId = newComments.Max(c => c.Id);
                    _stateTracker.RecordReviewResolution(prUrl, fingerprint, maxCommentId);
                    stateModified = true;
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

        // Save state if modified
        if (stateModified)
        {
            await _stateTracker.SaveStateAsync(cancellationToken);
            Console.WriteLine("State saved");
        }

        var nextRunTime = startTime.AddMinutes(_options.IntervalMinutes);
        Console.WriteLine($"\nNext check scheduled for: {nextRunTime:yyyy-MM-dd HH:mm:ss}");
    }
}
