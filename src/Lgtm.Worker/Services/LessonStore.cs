namespace Lgtm.Worker.Services;

/// <summary>
/// Manages per-repository lessons stored in ~/lgtm/lessons/{owner}/{repo}.md
/// </summary>
public class LessonStore : ILessonStore
{
    private readonly IClaudeInteractor _claudeInteractor;
    private readonly IGitHubClient _gitHubClient;
    private readonly ILessonExtractor _lessonExtractor;
    private const string LessonsBaseDir = "~/lgtm/lessons";
    private const int HistoricPrLimit = 10;

    public LessonStore(IClaudeInteractor claudeInteractor, IGitHubClient gitHubClient, ILessonExtractor lessonExtractor)
    {
        _claudeInteractor = claudeInteractor;
        _gitHubClient = gitHubClient;
        _lessonExtractor = lessonExtractor;
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

    /// <inheritdoc/>
    public async Task InitializeLessonsFromHistoryAsync(
        string owner, string repo, string? authorFilter, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Fetching last {HistoricPrLimit} PRs for {owner}/{repo}...");
        var prs = await _gitHubClient.GetRecentPrsAsync(owner, repo, HistoricPrLimit, authorFilter, cancellationToken);

        if (prs.Count == 0)
        {
            Console.WriteLine("No PRs found to extract lessons from");
            return;
        }

        Console.WriteLine($"Found {prs.Count} PRs, extracting lessons from review comments...");

        var lessonsExtracted = 0;
        var prsProcessed = 0;

        foreach (var pr in prs)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            prsProcessed++;
            Console.Write($"\rProcessing PR {prsProcessed}/{prs.Count}: #{pr.Number}...");

            var comments = await _gitHubClient.GetAllReviewCommentsAsync(owner, repo, pr.Number, cancellationToken);

            // Filter to thread roots only - replies should not generate separate lessons
            var threadRoots = comments.Where(c => c.InReplyToId is null).ToList();

            foreach (var comment in threadRoots)
            {
                // Skip self-comments (comments by the PR author)
                if (string.Equals(comment.Author, pr.Author, StringComparison.OrdinalIgnoreCase))
                    continue;

                var lesson = await _lessonExtractor.ExtractLessonAsync(comment, cancellationToken);
                if (!string.IsNullOrEmpty(lesson))
                {
                    await SaveLessonAsync(owner, repo, lesson, cancellationToken);
                    lessonsExtracted++;
                }
            }
        }

        Console.WriteLine(); // New line after progress
        Console.WriteLine($"Initialization complete: extracted {lessonsExtracted} lessons from {prsProcessed} PRs");
    }

    private static string GetLessonsFilePath(string owner, string repo)
    {
        var baseDir = PathUtilities.ExpandPath(LessonsBaseDir);
        return Path.Combine(baseDir, owner, $"{repo}.md");
    }
}
