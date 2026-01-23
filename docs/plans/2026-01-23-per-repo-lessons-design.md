# Per-Repository Lessons Design

## Overview

Add a feedback loop so Claude learns from PR review comments and doesn't repeat the same mistakes. Lessons are stored per-repository since different teams have different standards.

## How It Works

1. Review comment arrives on a PR
2. Claude extracts a general principle from the comment
3. Lesson is consolidated with existing lessons for that repo
4. Resolution prompt includes all lessons
5. Claude makes the fix with full context

```
Review comment arrives
        │
        ▼
Claude extracts lesson from the review comment
        │
        ▼
LessonStore consolidates and writes updated lessons
        │
        ▼
ResolutionPromptBuilder includes all lessons in prompt
        │
        ▼
Claude makes the fix (with full lessons context)
```

## New Components

### ILessonStore

Reads, writes, and consolidates per-repo lessons stored in `~/.lgtm/lessons/{owner}/{repo}.md`.

```csharp
public interface ILessonStore
{
    Task<string?> GetLessonsAsync(string owner, string repo);
    Task SaveLessonAsync(string owner, string repo, string newLesson);
}
```

- `GetLessonsAsync` - Returns markdown content for a repo (or null if no lessons yet)
- `SaveLessonAsync` - Calls Claude to consolidate new lesson with existing ones, writes the file

### ILessonExtractor

Extracts general principles from review comments.

```csharp
public interface ILessonExtractor
{
    Task<string> ExtractLessonAsync(ReviewComment comment);
}
```

### ResolutionPromptBuilder (Modified)

Takes lessons as an additional parameter and includes them in the prompt.

## Lessons File Format

Stored at `~/.lgtm/lessons/{owner}/{repo}.md`:

```markdown
# Lessons for {owner}/{repo}

## Error Handling

- Always use `await using` for disposables that involve async operations
- Wrap external API calls in try-catch and log failures before re-throwing

## Code Style

- Use primary constructors for simple dependency injection
- Prefer collection expressions (`[]`) over `new List<T>()`

## Testing

- Mock external services at the interface level, not the implementation
```

Categories are created dynamically by Claude based on content.

## Prompts

### Lesson Extraction

```
A reviewer left this comment on a pull request:

File: {path}, Line: {line}
{comment body}

Extract a general, reusable lesson from this feedback. The lesson should:
- Be actionable and specific (not vague like "write better code")
- Apply broadly, not just to this specific line
- Be one sentence

Return only the lesson, nothing else.
```

### Consolidation

```
Here are the existing lessons for this repository:

{existing markdown content}

Add this new lesson:
{new lesson}

Return the updated markdown file. You should:
- Add the lesson under an appropriate category (create one if needed)
- Merge with any existing lesson if they say the same thing
- Remove any lessons that conflict with newer ones
- Keep each category focused (max 5-7 lessons per category)

Return only the markdown content, nothing else.
```

### Modified Resolution Prompt

Includes a lessons section when lessons exist:

```
## Lessons learned for this repository

Keep these in mind while making changes - these are patterns this team cares about:

{markdown content from lessons file}
```

## Error Handling

| Scenario | Behavior |
|----------|----------|
| Lesson extraction fails | Log and continue with fix - don't block PR processing |
| Consolidation fails | Log and keep existing file unchanged |
| Lessons file corrupted | Claude can fix during consolidation, or start fresh |
| No lessons file exists | `GetLessonsAsync` returns null; `SaveLessonAsync` creates it |

Philosophy: Lessons are a "nice to have" enhancement. Failures shouldn't break core PR processing.

## Testing Strategy

### New Test Project

`tests/Lgtm.Worker.Tests`

### Unit Tests

**LessonExtractor:**
- Calls Claude with correct prompt format
- Returns Claude's response as the lesson

**LessonStore:**
- `GetLessonsAsync` returns null when file doesn't exist
- `GetLessonsAsync` returns content when file exists
- `SaveLessonAsync` creates directory structure if missing
- `SaveLessonAsync` calls Claude with existing content + new lesson
- `SaveLessonAsync` writes Claude's response to the file

**ResolutionPromptBuilder:**
- Prompt includes lessons section when lessons provided
- Prompt omits lessons section when lessons is null/empty

### Mocking Approach

- Mock `IClaudeInteractor` (or create a simpler interface for non-streaming Claude calls)
- Use temp directory for file system tests

## Success Metric

Over time, reviewers leave fewer comments about issues Claude has already learned about.
