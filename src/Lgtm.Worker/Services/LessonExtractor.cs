using Lgtm.Worker.Models;

namespace Lgtm.Worker.Services;

/// <summary>
/// Extracts general lessons from PR review comments using Claude.
/// </summary>
public class LessonExtractor : ILessonExtractor
{
    private readonly IClaudeInteractor _claudeInteractor;

    public LessonExtractor(IClaudeInteractor claudeInteractor)
    {
        _claudeInteractor = claudeInteractor;
    }

    /// <inheritdoc/>
    public async Task<string?> ExtractLessonAsync(ReviewComment comment, CancellationToken cancellationToken)
    {
        var location = string.IsNullOrEmpty(comment.Path)
            ? "General comment"
            : comment.Line.HasValue
                ? $"File: {comment.Path}, Line: {comment.Line}"
                : $"File: {comment.Path}";

        var prompt = $"""
            A reviewer left this comment on a pull request:

            {location}
            {comment.Body}

            Extract a general, reusable lesson from this feedback. The lesson should:
            - Be actionable and specific (not vague like "write better code")
            - Apply broadly, not just to this specific line
            - Be one sentence

            Return only the lesson, nothing else.
            """;

        try
        {
            var lesson = await _claudeInteractor.GetCompletionAsync(prompt, cancellationToken);
            return string.IsNullOrWhiteSpace(lesson) ? null : lesson;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to extract lesson: {ex.Message}");
            return null;
        }
    }
}
