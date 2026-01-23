using Lgtm.Worker.Models;

namespace Lgtm.Worker.Services;

/// <summary>
/// Provides operations for interacting with GitHub repositories and Pull Requests.
/// </summary>
public interface IGitHubClient
{
    /// <summary>
    /// Ensures a repository is cloned and the specified PR branch is checked out.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repoName">The repository name.</param>
    /// <param name="prNumber">The pull request number.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The path to the repository on the local filesystem, or null if the operation failed.</returns>
    Task<string?> EnsureRepoCheckedOutAsync(string owner, string repoName, int prNumber, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves the status of a GitHub Pull Request.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repo">The repository name.</param>
    /// <param name="prNumber">The pull request number.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The PR status, or null if the operation failed.</returns>
    Task<PrStatus?> GetPrStatusAsync(string owner, string repo, int prNumber, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves review comments from a Pull Request that were created after a specific date.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repo">The repository name.</param>
    /// <param name="prNumber">The pull request number.</param>
    /// <param name="sinceDate">Only return comments created after this date/time. If null, returns all comments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of review comments (may be empty if none found or operation failed).</returns>
    Task<List<ReviewComment>> GetNewReviewCommentsAsync(string owner, string repo, int prNumber, DateTimeOffset? sinceDate, CancellationToken cancellationToken);

    /// <summary>
    /// Converts a Pull Request to draft status.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repo">The repository name.</param>
    /// <param name="prNumber">The pull request number.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the conversion succeeded, false otherwise.</returns>
    Task<bool> ConvertToDraftAsync(string owner, string repo, int prNumber, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves review comments from a Pull Request with IDs greater than a specified threshold.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repo">The repository name.</param>
    /// <param name="prNumber">The pull request number.</param>
    /// <param name="afterId">Only return comments with ID greater than this value. If null, returns all comments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of review comments (may be empty if none found or operation failed).</returns>
    Task<List<ReviewComment>> GetReviewCommentsAfterIdAsync(string owner, string repo, int prNumber, long? afterId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the ID of the latest review comment on a Pull Request.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repo">The repository name.</param>
    /// <param name="prNumber">The pull request number.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ID of the latest comment, or null if no comments exist.</returns>
    Task<long?> GetLatestCommentIdAsync(string owner, string repo, int prNumber, CancellationToken cancellationToken);

    /// <summary>
    /// Gets all open Pull Requests in a repository by a specific author.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repo">The repository name.</param>
    /// <param name="author">The GitHub username of the PR author.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of open PRs by the specified author.</returns>
    Task<List<OpenPrInfo>> GetOpenPrsByAuthorAsync(string owner, string repo, string author, CancellationToken cancellationToken);

    /// <summary>
    /// Gets recent Pull Requests from a repository.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repo">The repository name.</param>
    /// <param name="limit">Maximum number of PRs to return.</param>
    /// <param name="authorFilter">Optional author filter. If specified, only returns PRs by this author.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of recent PRs.</returns>
    Task<List<PrListItem>> GetRecentPrsAsync(string owner, string repo, int limit, string? authorFilter, CancellationToken cancellationToken);

    /// <summary>
    /// Gets all review comments from a Pull Request.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repo">The repository name.</param>
    /// <param name="prNumber">The pull request number.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of all review comments.</returns>
    Task<List<ReviewComment>> GetAllReviewCommentsAsync(string owner, string repo, int prNumber, CancellationToken cancellationToken);
}
