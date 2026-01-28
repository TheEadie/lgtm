using Lgtm.Worker.Models;
using Lgtm.Worker.Services;

namespace Lgtm.Worker.Tests;

public class ResolutionPromptBuilderTests
{
    private readonly ResolutionPromptBuilder _sut;

    public ResolutionPromptBuilderTests()
    {
        _sut = new ResolutionPromptBuilder();
    }

    [Fact]
    public void BuildReviewResolutionPrompt_IncludesLessonsSection_WhenLessonsProvided()
    {
        // Arrange
        var comments = new List<ReviewComment>
        {
            new(Id: 1, Author: "reviewer", Path: "test.cs", Line: 10, Body: "Fix this", CreatedAt: DateTimeOffset.Now, InReplyToId: null)
        };
        var lessons = "## Code Style\n\n- Use async/await";

        // Act
        var result = _sut.BuildReviewResolutionPrompt("feature-branch", comments, lessons);

        // Assert
        Assert.Contains("## Lessons learned for this repository", result);
        Assert.Contains("Keep these in mind while making changes", result);
        Assert.Contains("## Code Style", result);
        Assert.Contains("- Use async/await", result);
    }

    [Fact]
    public void BuildReviewResolutionPrompt_OmitsLessonsSection_WhenLessonsIsNull()
    {
        // Arrange
        var comments = new List<ReviewComment>
        {
            new(Id: 1, Author: "reviewer", Path: "test.cs", Line: 10, Body: "Fix this", CreatedAt: DateTimeOffset.Now, InReplyToId: null)
        };

        // Act
        var result = _sut.BuildReviewResolutionPrompt("feature-branch", comments, null);

        // Assert
        Assert.DoesNotContain("## Lessons learned for this repository", result);
        Assert.DoesNotContain("Keep these in mind while making changes", result);
    }

    [Fact]
    public void BuildReviewResolutionPrompt_OmitsLessonsSection_WhenLessonsIsEmpty()
    {
        // Arrange
        var comments = new List<ReviewComment>
        {
            new(Id: 1, Author: "reviewer", Path: "test.cs", Line: 10, Body: "Fix this", CreatedAt: DateTimeOffset.Now, InReplyToId: null)
        };

        // Act
        var result = _sut.BuildReviewResolutionPrompt("feature-branch", comments, "");

        // Assert
        Assert.DoesNotContain("## Lessons learned for this repository", result);
    }

    [Fact]
    public void BuildReviewResolutionPrompt_OmitsLessonsSection_WhenLessonsIsWhitespace()
    {
        // Arrange
        var comments = new List<ReviewComment>
        {
            new(Id: 1, Author: "reviewer", Path: "test.cs", Line: 10, Body: "Fix this", CreatedAt: DateTimeOffset.Now, InReplyToId: null)
        };

        // Act
        var result = _sut.BuildReviewResolutionPrompt("feature-branch", comments, "   ");

        // Assert
        Assert.DoesNotContain("## Lessons learned for this repository", result);
    }

    [Fact]
    public void BuildReviewResolutionPrompt_IncludesCommentDetails()
    {
        // Arrange
        var comments = new List<ReviewComment>
        {
            new(Id: 1, Author: "reviewer", Path: "src/test.cs", Line: 42, Body: "Please fix this bug", CreatedAt: DateTimeOffset.Now, InReplyToId: null)
        };

        // Act
        var result = _sut.BuildReviewResolutionPrompt("feature-branch", comments);

        // Assert
        Assert.Contains("[reviewer]", result);
        Assert.Contains("File: src/test.cs, Line: 42", result);
        Assert.Contains("Please fix this bug", result);
    }

    [Fact]
    public void BuildReviewResolutionPrompt_IncludesBranchName()
    {
        // Arrange
        var comments = new List<ReviewComment>
        {
            new(Id: 1, Author: "reviewer", Path: "test.cs", Line: 10, Body: "Fix this", CreatedAt: DateTimeOffset.Now, InReplyToId: null)
        };

        // Act
        var result = _sut.BuildReviewResolutionPrompt("my-feature-branch", comments);

        // Assert
        Assert.Contains("branch 'my-feature-branch'", result);
        Assert.Contains("git push origin my-feature-branch", result);
    }

    [Fact]
    public void BuildReviewResolutionPrompt_LessonsAppearBeforeComments()
    {
        // Arrange
        var comments = new List<ReviewComment>
        {
            new(Id: 1, Author: "reviewer", Path: "test.cs", Line: 10, Body: "Fix this", CreatedAt: DateTimeOffset.Now, InReplyToId: null)
        };
        var lessons = "## Code Style\n\n- Important lesson";

        // Act
        var result = _sut.BuildReviewResolutionPrompt("feature-branch", comments, lessons);

        // Assert
        var lessonsIndex = result.IndexOf("## Lessons learned for this repository");
        var commentsIndex = result.IndexOf("Review comments:");
        Assert.True(lessonsIndex < commentsIndex, "Lessons should appear before review comments");
    }
}
