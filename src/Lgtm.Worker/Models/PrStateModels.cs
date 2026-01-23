namespace Lgtm.Worker.Models;

/// <summary>
/// Captures a snapshot of a PR's state at a specific point in time.
/// Used to determine if meaningful changes have occurred since last processing.
/// </summary>
/// <param name="HeadCommitSha">The SHA of the head commit.</param>
/// <param name="MergeableState">The mergeable state (e.g., "MERGEABLE", "CONFLICTING", "UNKNOWN").</param>
/// <param name="IsDraft">Whether the PR is in draft status.</param>
/// <param name="LatestCommentId">The ID of the most recent review comment, if any.</param>
/// <param name="CapturedAt">When this fingerprint was captured.</param>
public record PrStateFingerprint(
    string HeadCommitSha,
    string MergeableState,
    bool IsDraft,
    long? LatestCommentId,
    DateTimeOffset CapturedAt);

/// <summary>
/// Tracks the processed state for a single PR.
/// </summary>
/// <param name="PrUrl">The full URL of the pull request.</param>
/// <param name="LastConflictResolutionState">State when conflicts were last resolved, if ever.</param>
/// <param name="LastReviewResolutionState">State when reviews were last addressed, if ever.</param>
/// <param name="LastAddressedCommentId">The ID of the last review comment that was addressed.</param>
/// <param name="LastProcessedAt">When this PR was last processed for any reason.</param>
/// <param name="SeenAsMergedAt">When the PR was first observed as merged, if ever.</param>
public record ProcessedPrState(
    string PrUrl,
    PrStateFingerprint? LastConflictResolutionState,
    PrStateFingerprint? LastReviewResolutionState,
    long? LastAddressedCommentId,
    DateTimeOffset? LastProcessedAt,
    DateTimeOffset? SeenAsMergedAt);

/// <summary>
/// Root object for the persisted state file.
/// </summary>
/// <param name="Version">Schema version for future compatibility.</param>
/// <param name="States">Map of PR URL to its processed state.</param>
/// <param name="LastUpdatedAt">When the state file was last updated.</param>
public record PrStateStore(
    int Version,
    Dictionary<string, ProcessedPrState> States,
    DateTimeOffset LastUpdatedAt);
