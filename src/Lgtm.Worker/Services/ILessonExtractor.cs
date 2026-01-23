using Lgtm.Worker.Models;

namespace Lgtm.Worker.Services;

/// <summary>
/// Extracts general lessons from PR review comments.
/// </summary>
public interface ILessonExtractor
{
    /// <summary>
    /// Extracts a general, reusable lesson from a review comment.
    /// </summary>
    /// <param name="comment">The review comment to extract a lesson from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A generalized lesson, or null if extraction fails.</returns>
    Task<string?> ExtractLessonAsync(ReviewComment comment, CancellationToken cancellationToken);
}
