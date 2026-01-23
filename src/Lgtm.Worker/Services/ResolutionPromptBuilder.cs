using Lgtm.Worker.Models;

namespace Lgtm.Worker.Services;

/// <summary>
/// Provides methods for building prompts to resolve PR conflicts and review comments.
/// </summary>
public class ResolutionPromptBuilder : IResolutionPromptBuilder
{
    /// <inheritdoc/>
    public string BuildConflictResolutionPrompt(string headRefName, string baseRefName)
    {
        return $"""
            The PR branch '{headRefName}' has merge conflicts with '{baseRefName}'.
            Please:
            1. Run: git fetch origin
            2. Run: git rebase origin/{baseRefName}
            3. Resolve any merge conflicts that arise
            4. After resolving, run: git rebase --continue
            5. Once complete, run: git push --force-with-lease origin {headRefName}

            IMPORTANT: You are working on branch '{headRefName}'. Never force push to main or develop.
            """;
    }

    /// <inheritdoc/>
    public string BuildReviewResolutionPrompt(string headRefName, List<ReviewComment> comments, string? lessons = null)
    {
        var commentDescriptions = string.Join("\n\n", comments.Select(c =>
        {
            var location = string.IsNullOrEmpty(c.Path)
                ? "General comment"
                : c.Line.HasValue
                    ? $"File: {c.Path}, Line: {c.Line}"
                    : $"File: {c.Path}";
            return $"[{c.Author}] {location}\n{c.Body}";
        }));

        var lessonsSection = string.IsNullOrWhiteSpace(lessons)
            ? ""
            : $"""

            ## Lessons learned for this repository

            Keep these in mind while making changes - these are patterns this team cares about:

            {lessons}

            """;

        return $"""
            There are new review comments on the PR for branch '{headRefName}' that need to be addressed.
            {lessonsSection}
            Review comments:
            {commentDescriptions}

            Please:
            1. Read and understand each review comment
            2. Make the necessary code changes to address the feedback
            3. Commit your changes with a descriptive message referencing the review feedback
            4. Push your changes: git push origin {headRefName}

            IMPORTANT: You are working on branch '{headRefName}'. Do not force push - use a regular push.
            """;
    }
}
