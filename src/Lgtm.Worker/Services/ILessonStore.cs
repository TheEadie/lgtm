namespace Lgtm.Worker.Services;

/// <summary>
/// Manages per-repository lessons learned from PR review feedback.
/// </summary>
public interface ILessonStore
{
    /// <summary>
    /// Gets the lessons markdown content for a repository.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repo">The repository name.</param>
    /// <returns>The markdown content, or null if no lessons exist.</returns>
    Task<string?> GetLessonsAsync(string owner, string repo);

    /// <summary>
    /// Saves a new lesson, consolidating it with existing lessons.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repo">The repository name.</param>
    /// <param name="newLesson">The new lesson to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SaveLessonAsync(string owner, string repo, string newLesson, CancellationToken cancellationToken);
}
