using Lgtm.Worker.Models;
using Lgtm.Worker.Services;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Lgtm.Worker.Tests;

public class LessonExtractorTests
{
    private readonly IClaudeInteractor _claudeInteractor;
    private readonly LessonExtractor _sut;

    public LessonExtractorTests()
    {
        _claudeInteractor = Substitute.For<IClaudeInteractor>();
        _sut = new LessonExtractor(_claudeInteractor);
    }

    [Fact]
    public async Task ExtractLessonAsync_CallsClaudeWithCorrectPromptFormat()
    {
        // Arrange
        var comment = new ReviewComment(
            Id: 123,
            Author: "reviewer",
            Path: "src/Example.cs",
            Line: 42,
            Body: "Use await using here",
            CreatedAt: DateTimeOffset.Now);

        _claudeInteractor.GetCompletionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("Always use await using for disposables");

        // Act
        await _sut.ExtractLessonAsync(comment, CancellationToken.None);

        // Assert
        await _claudeInteractor.Received(1).GetCompletionAsync(
            Arg.Is<string>(prompt =>
                prompt.Contains("File: src/Example.cs, Line: 42") &&
                prompt.Contains("Use await using here") &&
                prompt.Contains("Extract a general, reusable lesson")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractLessonAsync_ReturnsClaudeResponse()
    {
        // Arrange
        var comment = new ReviewComment(
            Id: 123,
            Author: "reviewer",
            Path: "src/Example.cs",
            Line: 42,
            Body: "Use await using here",
            CreatedAt: DateTimeOffset.Now);

        var expectedLesson = "Always use await using for disposables";
        _claudeInteractor.GetCompletionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(expectedLesson);

        // Act
        var result = await _sut.ExtractLessonAsync(comment, CancellationToken.None);

        // Assert
        Assert.Equal(expectedLesson, result);
    }

    [Fact]
    public async Task ExtractLessonAsync_ReturnsNull_WhenClaudeFails()
    {
        // Arrange
        var comment = new ReviewComment(
            Id: 123,
            Author: "reviewer",
            Path: "src/Example.cs",
            Line: 42,
            Body: "Use await using here",
            CreatedAt: DateTimeOffset.Now);

        _claudeInteractor.GetCompletionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Claude failed"));

        // Act
        var result = await _sut.ExtractLessonAsync(comment, CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ExtractLessonAsync_ReturnsNull_WhenClaudeReturnsEmpty()
    {
        // Arrange
        var comment = new ReviewComment(
            Id: 123,
            Author: "reviewer",
            Path: "src/Example.cs",
            Line: 42,
            Body: "Use await using here",
            CreatedAt: DateTimeOffset.Now);

        _claudeInteractor.GetCompletionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("   ");

        // Act
        var result = await _sut.ExtractLessonAsync(comment, CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ExtractLessonAsync_HandlesCommentWithoutLine()
    {
        // Arrange
        var comment = new ReviewComment(
            Id: 123,
            Author: "reviewer",
            Path: "src/Example.cs",
            Line: null,
            Body: "This file needs refactoring",
            CreatedAt: DateTimeOffset.Now);

        _claudeInteractor.GetCompletionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("Consider refactoring large files");

        // Act
        await _sut.ExtractLessonAsync(comment, CancellationToken.None);

        // Assert
        await _claudeInteractor.Received(1).GetCompletionAsync(
            Arg.Is<string>(prompt => prompt.Contains("File: src/Example.cs") && !prompt.Contains("Line:")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExtractLessonAsync_HandlesGeneralComment()
    {
        // Arrange
        var comment = new ReviewComment(
            Id: 123,
            Author: "reviewer",
            Path: "",
            Line: null,
            Body: "Overall good work",
            CreatedAt: DateTimeOffset.Now);

        _claudeInteractor.GetCompletionAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("Keep up the good practices");

        // Act
        await _sut.ExtractLessonAsync(comment, CancellationToken.None);

        // Assert
        await _claudeInteractor.Received(1).GetCompletionAsync(
            Arg.Is<string>(prompt => prompt.Contains("General comment")),
            Arg.Any<CancellationToken>());
    }
}
