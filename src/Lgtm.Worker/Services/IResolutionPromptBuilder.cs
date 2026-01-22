using Lgtm.Worker.Models;

namespace Lgtm.Worker.Services;

/// <summary>
/// Provides methods for building prompts to resolve PR conflicts and review comments.
/// </summary>
public interface IResolutionPromptBuilder
{
    /// <summary>
    /// Builds a prompt for Claude to resolve merge conflicts.
    /// </summary>
    /// <param name="headRefName">The name of the head branch (source branch with conflicts).</param>
    /// <param name="baseRefName">The name of the base branch (target branch to rebase onto).</param>
    /// <returns>A prompt instructing Claude how to resolve the conflicts.</returns>
    string BuildConflictResolutionPrompt(string headRefName, string baseRefName);

    /// <summary>
    /// Builds a prompt for Claude to address review comments.
    /// </summary>
    /// <param name="headRefName">The name of the head branch (source branch).</param>
    /// <param name="comments">The list of review comments to address.</param>
    /// <returns>A prompt instructing Claude how to address the review comments.</returns>
    string BuildReviewResolutionPrompt(string headRefName, List<ReviewComment> comments);
}
