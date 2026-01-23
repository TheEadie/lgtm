namespace Lgtm.Worker.Services;

/// <summary>
/// Manages per-repository lessons stored in ~/.lgtm/lessons/{owner}/{repo}.md
/// </summary>
public class LessonStore : ILessonStore
{
    private readonly IClaudeInteractor _claudeInteractor;
    private const string LessonsBaseDir = "~/.lgtm/lessons";

    public LessonStore(IClaudeInteractor claudeInteractor)
    {
        _claudeInteractor = claudeInteractor;
    }

    /// <inheritdoc/>
    public Task<string?> GetLessonsAsync(string owner, string repo)
    {
        var filePath = GetLessonsFilePath(owner, repo);

        if (!File.Exists(filePath))
            return Task.FromResult<string?>(null);

        var content = File.ReadAllText(filePath);
        return Task.FromResult<string?>(string.IsNullOrWhiteSpace(content) ? null : content);
    }

    /// <inheritdoc/>
    public async Task SaveLessonAsync(string owner, string repo, string newLesson, CancellationToken cancellationToken)
    {
        var filePath = GetLessonsFilePath(owner, repo);
        var existingContent = await GetLessonsAsync(owner, repo);

        string updatedContent;
        if (string.IsNullOrEmpty(existingContent))
        {
            // No existing lessons - create initial file with just this lesson
            updatedContent = await CreateInitialLessonsAsync(owner, repo, newLesson, cancellationToken);
        }
        else
        {
            // Consolidate with existing lessons
            updatedContent = await ConsolidateLessonsAsync(existingContent, newLesson, cancellationToken);
        }

        // Ensure directory exists
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(filePath, updatedContent, cancellationToken);
        Console.WriteLine($"Saved lesson to {filePath}");
    }

    private async Task<string> CreateInitialLessonsAsync(string owner, string repo, string lesson, CancellationToken cancellationToken)
    {
        var prompt = $"""
            Create a lessons file for the repository {owner}/{repo}.

            Add this lesson:
            {lesson}

            Return a markdown file with:
            - A header "# Lessons for {owner}/{repo}"
            - The lesson under an appropriate category heading (e.g., "## Code Style", "## Error Handling", etc.)
            - The lesson as a bullet point

            Return only the markdown content, nothing else.
            """;

        try
        {
            return await _claudeInteractor.GetCompletionAsync(prompt, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create initial lessons file: {ex.Message}");
            // Fall back to a simple format
            return $"""
                # Lessons for {owner}/{repo}

                ## General

                - {lesson}
                """;
        }
    }

    private async Task<string> ConsolidateLessonsAsync(string existingContent, string newLesson, CancellationToken cancellationToken)
    {
        var prompt = $"""
            Here are the existing lessons for this repository:

            {existingContent}

            Add this new lesson:
            {newLesson}

            Return the updated markdown file. You should:
            - Add the lesson under an appropriate category (create one if needed)
            - Merge with any existing lesson if they say the same thing
            - Remove any lessons that conflict with newer ones
            - Keep each category focused (max 5-7 lessons per category)

            Return only the markdown content, nothing else.
            """;

        try
        {
            return await _claudeInteractor.GetCompletionAsync(prompt, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to consolidate lessons: {ex.Message}");
            // Return existing content unchanged if consolidation fails
            return existingContent;
        }
    }

    private static string GetLessonsFilePath(string owner, string repo)
    {
        var baseDir = PathUtilities.ExpandPath(LessonsBaseDir);
        return Path.Combine(baseDir, owner, $"{repo}.md");
    }
}
