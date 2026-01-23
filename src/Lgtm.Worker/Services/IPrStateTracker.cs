using Lgtm.Worker.Models;

namespace Lgtm.Worker.Services;

/// <summary>
/// Tracks PR state to determine when processing is needed and records completed work.
/// </summary>
public interface IPrStateTracker
{
    /// <summary>
    /// Loads the state store from disk. Creates an empty store if none exists.
    /// </summary>
    Task LoadStateAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Persists the current state store to disk.
    /// </summary>
    Task SaveStateAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Builds a fingerprint of the current PR state from GitHub data.
    /// </summary>
    /// <param name="prUrl">The PR URL.</param>
    /// <param name="status">The current PR status from GitHub.</param>
    /// <param name="latestCommentId">The ID of the most recent review comment, if any.</param>
    PrStateFingerprint GetCurrentFingerprint(string prUrl, PrStatus status, long? latestCommentId);

    /// <summary>
    /// Determines if conflict resolution should be performed based on current and previous state.
    /// </summary>
    /// <param name="prUrl">The PR URL.</param>
    /// <param name="currentFingerprint">The current state fingerprint.</param>
    /// <returns>True if conflicts should be resolved.</returns>
    bool ShouldResolveConflicts(string prUrl, PrStateFingerprint currentFingerprint);

    /// <summary>
    /// Determines if review comments should be addressed based on current and previous state.
    /// </summary>
    /// <param name="prUrl">The PR URL.</param>
    /// <param name="latestCommentId">The ID of the most recent comment.</param>
    /// <returns>True if reviews should be addressed.</returns>
    bool ShouldAddressReviews(string prUrl, long? latestCommentId);

    /// <summary>
    /// Gets the ID of the last addressed comment for a PR, if any.
    /// </summary>
    /// <param name="prUrl">The PR URL.</param>
    /// <returns>The last addressed comment ID, or null if none.</returns>
    long? GetLastAddressedCommentId(string prUrl);

    /// <summary>
    /// Records that conflict resolution was performed for a PR.
    /// </summary>
    /// <param name="prUrl">The PR URL.</param>
    /// <param name="fingerprint">The state fingerprint at the time of resolution.</param>
    void RecordConflictResolution(string prUrl, PrStateFingerprint fingerprint);

    /// <summary>
    /// Records that review comments were addressed for a PR.
    /// </summary>
    /// <param name="prUrl">The PR URL.</param>
    /// <param name="fingerprint">The state fingerprint at the time of resolution.</param>
    /// <param name="lastAddressedCommentId">The ID of the last comment that was addressed.</param>
    void RecordReviewResolution(string prUrl, PrStateFingerprint fingerprint, long lastAddressedCommentId);

    /// <summary>
    /// Checks if this is the first time we're seeing this PR as merged.
    /// </summary>
    /// <param name="prUrl">The PR URL.</param>
    /// <returns>True if the PR has not been seen as merged before.</returns>
    bool IsFirstSeenAsMerged(string prUrl);

    /// <summary>
    /// Records that a PR has been seen as merged.
    /// </summary>
    /// <param name="prUrl">The PR URL.</param>
    void RecordMerge(string prUrl);

    /// <summary>
    /// Gets all PR URLs that are currently being tracked and haven't been marked as merged.
    /// Used to ensure we continue monitoring PRs until we see their merge.
    /// </summary>
    /// <returns>A collection of tracked PR URLs.</returns>
    IEnumerable<string> GetTrackedPrUrls();
}
