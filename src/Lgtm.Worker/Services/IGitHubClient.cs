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
}
