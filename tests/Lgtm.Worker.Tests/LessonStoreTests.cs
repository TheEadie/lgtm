using Lgtm.Worker.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Lgtm.Worker.Tests;

public class LessonStoreTests : IDisposable
{
    private readonly IClaudeInteractor _claudeInteractor;
    private readonly IGitHubClient _gitHubClient;
    private readonly ILessonExtractor _lessonExtractor;
    private readonly string _tempDir;
    private readonly string _originalHome;

    public LessonStoreTests()
    {
        _claudeInteractor = Substitute.For<IClaudeInteractor>();
        _gitHubClient = Substitute.For<IGitHubClient>();
        _lessonExtractor = Substitute.For<ILessonExtractor>();
        _tempDir = Path.Combine(Path.GetTempPath(), $"lgtm-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);

        // Store original HOME and set to temp dir for testing
        _originalHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Environment.SetEnvironmentVariable("HOME", _tempDir);
    }

    public void Dispose()
    {
        // Restore original HOME
        Environment.SetEnvironmentVariable("HOME", _originalHome);

        // Clean up temp directory
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetLessonsAsync_ReturnsNull_WhenFileDoesNotExist()
    {
        // Arrange
        var sut = new LessonStore(_claudeInteractor, _gitHubClient, _lessonExtractor);

        // Act
        var result = await sut.GetLessonsAsync("owner", "repo");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetLessonsAsync_ReturnsContent_WhenFileExists()
    {
        // Arrange
        var lessonsDir = Path.Combine(_tempDir, "lgtm", "lessons", "owner");
        Directory.CreateDirectory(lessonsDir);
        var filePath = Path.Combine(lessonsDir, "repo.md");
        var expectedContent = "# Lessons\n\n- Use async/await";
        await File.WriteAllTextAsync(filePath, expectedContent);

        var sut = new LessonStore(_claudeInteractor, _gitHubClient, _lessonExtractor);

        // Act
        var result = await sut.GetLessonsAsync("owner", "repo");

        // Assert
        Assert.Equal(expectedContent, result);
    }

    [Fact]
    public async Task GetLessonsAsync_ReturnsNull_WhenFileIsEmpty()
    {
        // Arrange
        var lessonsDir = Path.Combine(_tempDir, "lgtm", "lessons", "owner");
        Directory.CreateDirectory(lessonsDir);
        var filePath = Path.Combine(lessonsDir, "repo.md");
        await File.WriteAllTextAsync(filePath, "   ");

        var sut = new LessonStore(_claudeInteractor, _gitHubClient, _lessonExtractor);

        // Act
        var result = await sut.GetLessonsAsync("owner", "repo");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task SaveLessonAsync_CreatesDirectoryStructure_WhenMissing()
    {
        // Arrange
        var sut = new LessonStore(_claudeInteractor, _gitHubClient, _lessonExtractor);
        _claudeInteractor.GetCompletionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("# Lessons for owner/repo\n\n## General\n\n- Test lesson");

        // Act
        await sut.SaveLessonAsync("owner", "repo", "Test lesson", CancellationToken.None);

        // Assert
        var filePath = Path.Combine(_tempDir, "lgtm", "lessons", "owner", "repo.md");
        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public async Task SaveLessonAsync_CallsClaudeToCreateInitialFile_WhenNoExistingLessons()
    {
        // Arrange
        var sut = new LessonStore(_claudeInteractor, _gitHubClient, _lessonExtractor);
        _claudeInteractor.GetCompletionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("# Lessons for owner/repo\n\n## General\n\n- Test lesson");

        // Act
        await sut.SaveLessonAsync("owner", "repo", "Test lesson", CancellationToken.None);

        // Assert
        await _claudeInteractor.Received(1).GetCompletionAsync(
            Arg.Is<string>(prompt =>
                prompt.Contains("Create a lessons file") &&
                prompt.Contains("owner/repo") &&
                prompt.Contains("Test lesson")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveLessonAsync_CallsClaudeToConsolidate_WhenExistingLessons()
    {
        // Arrange
        var lessonsDir = Path.Combine(_tempDir, "lgtm", "lessons", "owner");
        Directory.CreateDirectory(lessonsDir);
        var filePath = Path.Combine(lessonsDir, "repo.md");
        var existingContent = "# Lessons for owner/repo\n\n## Code Style\n\n- Existing lesson";
        await File.WriteAllTextAsync(filePath, existingContent);

        var sut = new LessonStore(_claudeInteractor, _gitHubClient, _lessonExtractor);
        _claudeInteractor.GetCompletionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("# Lessons for owner/repo\n\n## Code Style\n\n- Existing lesson\n- New lesson");

        // Act
        await sut.SaveLessonAsync("owner", "repo", "New lesson", CancellationToken.None);

        // Assert
        await _claudeInteractor.Received(1).GetCompletionAsync(
            Arg.Is<string>(prompt =>
                prompt.Contains("existing lessons") &&
                prompt.Contains(existingContent) &&
                prompt.Contains("New lesson") &&
                prompt.Contains("Merge with any existing lesson")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveLessonAsync_WritesClaudeResponse_ToFile()
    {
        // Arrange
        var sut = new LessonStore(_claudeInteractor, _gitHubClient, _lessonExtractor);
        var expectedContent = "# Lessons for owner/repo\n\n## Testing\n\n- Test lesson";
        _claudeInteractor.GetCompletionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(expectedContent);

        // Act
        await sut.SaveLessonAsync("owner", "repo", "Test lesson", CancellationToken.None);

        // Assert
        var filePath = Path.Combine(_tempDir, "lgtm", "lessons", "owner", "repo.md");
        var actualContent = await File.ReadAllTextAsync(filePath);
        Assert.Equal(expectedContent, actualContent);
    }

    [Fact]
    public async Task SaveLessonAsync_FallsBackToSimpleFormat_WhenClaudeFails_ForNewFile()
    {
        // Arrange
        var sut = new LessonStore(_claudeInteractor, _gitHubClient, _lessonExtractor);
        _claudeInteractor.GetCompletionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Claude failed"));

        // Act
        await sut.SaveLessonAsync("owner", "repo", "Test lesson", CancellationToken.None);

        // Assert
        var filePath = Path.Combine(_tempDir, "lgtm", "lessons", "owner", "repo.md");
        var content = await File.ReadAllTextAsync(filePath);
        Assert.Contains("# Lessons for owner/repo", content);
        Assert.Contains("## General", content);
        Assert.Contains("- Test lesson", content);
    }

    [Fact]
    public async Task SaveLessonAsync_KeepsExistingContent_WhenConsolidationFails()
    {
        // Arrange
        var lessonsDir = Path.Combine(_tempDir, "lgtm", "lessons", "owner");
        Directory.CreateDirectory(lessonsDir);
        var filePath = Path.Combine(lessonsDir, "repo.md");
        var existingContent = "# Lessons for owner/repo\n\n## Code Style\n\n- Existing lesson";
        await File.WriteAllTextAsync(filePath, existingContent);

        var sut = new LessonStore(_claudeInteractor, _gitHubClient, _lessonExtractor);
        _claudeInteractor.GetCompletionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Claude failed"));

        // Act
        await sut.SaveLessonAsync("owner", "repo", "New lesson", CancellationToken.None);

        // Assert
        var actualContent = await File.ReadAllTextAsync(filePath);
        Assert.Equal(existingContent, actualContent);
    }
}
