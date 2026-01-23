namespace Lgtm.Worker.Models;

/// <summary>
/// Represents information about a GitHub Pull Request extracted from a URL.
/// </summary>
/// <param name="Owner">The repository owner (username or organization).</param>
/// <param name="Repo">The repository name.</param>
/// <param name="PrNumber">The pull request number.</param>
public record PrInfo(string Owner, string Repo, int PrNumber);

/// <summary>
/// Represents the status of a GitHub Pull Request.
/// </summary>
/// <param name="State">The PR state (e.g., "OPEN", "CLOSED", "MERGED").</param>
/// <param name="Mergeable">The mergeable state (e.g., "MERGEABLE", "CONFLICTING", "UNKNOWN").</param>
/// <param name="HeadRefName">The name of the head branch (source branch).</param>
/// <param name="BaseRefName">The name of the base branch (target branch).</param>
/// <param name="HeadCommitSha">The SHA of the head commit.</param>
/// <param name="LatestCommitDate">The date/time of the most recent commit, if available.</param>
/// <param name="IsDraft">Whether the PR is in draft status.</param>
public record PrStatus(
    string State,
    string Mergeable,
    string HeadRefName,
    string BaseRefName,
    string HeadCommitSha,
    DateTimeOffset? LatestCommitDate,
    bool IsDraft);

/// <summary>
/// Represents a review submitted on a Pull Request.
/// </summary>
/// <param name="Author">The username of the review author.</param>
/// <param name="State">The review state (e.g., "APPROVED", "CHANGES_REQUESTED", "COMMENTED").</param>
/// <param name="Body">The review body text.</param>
/// <param name="SubmittedAt">The date/time when the review was submitted.</param>
public record PrReview(
    string Author,
    string State,
    string Body,
    DateTimeOffset SubmittedAt);

/// <summary>
/// Represents a review comment on specific lines of code in a Pull Request.
/// </summary>
/// <param name="Id">The unique identifier of the comment.</param>
/// <param name="Author">The username of the comment author.</param>
/// <param name="Path">The file path the comment refers to.</param>
/// <param name="Line">The line number the comment refers to, if applicable.</param>
/// <param name="Body">The comment body text.</param>
/// <param name="CreatedAt">The date/time when the comment was created.</param>
public record ReviewComment(
    long Id,
    string Author,
    string Path,
    int? Line,
    string Body,
    DateTimeOffset CreatedAt);

/// <summary>
/// Represents basic information about an open Pull Request.
/// </summary>
/// <param name="Number">The pull request number.</param>
/// <param name="Url">The full URL to the pull request.</param>
public record OpenPrInfo(int Number, string Url);

/// <summary>
/// Represents a pull request item from a list query.
/// </summary>
/// <param name="Number">The pull request number.</param>
/// <param name="Title">The pull request title.</param>
/// <param name="State">The pull request state (e.g., "open", "closed", "merged").</param>
/// <param name="Author">The username of the PR author.</param>
public record PrListItem(int Number, string Title, string State, string Author);
