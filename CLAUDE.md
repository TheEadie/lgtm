# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

LGTM is an automated PR maintenance worker that monitors GitHub pull requests and uses Claude CLI to:
- Resolve merge conflicts by rebasing branches
- Address review comments and make code changes
- Send notifications for PR status changes

It runs as a .NET 10 background service, polling configured PRs at intervals (default: 10 minutes).

## Build and Run

```bash
# Build
dotnet build Lgtm.sln

# Run with default config (~/lgtm/config.json)
dotnet run --project src/Lgtm.Worker

# Run with custom config
dotnet run --project src/Lgtm.Worker ./my-config.json
```

No test projects exist currently.

## Architecture

```
ScheduledWorkerService (BackgroundService)
    │
    ▼
WorkProcessor (main orchestration)
    ├── IGitHubClient (gh CLI, git commands, GitHub API)
    ├── IClaudeInteractor (Claude CLI invocation)
    ├── IPrStateTracker (persistent state in ~/.lgtm/state.json)
    ├── IResolutionPromptBuilder (prompt generation)
    └── INotificationService (Ntfy push notifications)
```

**Key data flow:**
1. Query PR status via `gh` CLI (no repo checkout needed)
2. Compare against stored state fingerprint
3. If work needed: clone/checkout repo, invoke Claude with restricted tools
4. Update state to prevent duplicate processing

## Key Files

- `src/Lgtm.Worker/Program.cs` - DI setup and entry point
- `src/Lgtm.Worker/Services/WorkProcessor.cs` - Main orchestration logic
- `src/Lgtm.Worker/Services/GitHubClient.cs` - GitHub CLI/API interactions
- `src/Lgtm.Worker/Services/ClaudeInteractor.cs` - Claude CLI execution
- `src/Lgtm.Worker/Services/PrStateTracker.cs` - State management to prevent duplicate work

## State Management

`PrStateTracker` prevents duplicate work by tracking per-PR state in `~/.lgtm/state.json`:
- Head commit SHA, mergeable state, draft status
- Latest comment ID addressed
- State fingerprints for conflict/review resolution

Processing only occurs if the fingerprint differs from stored state.

## External Dependencies

Required:
- .NET 10 SDK
- Claude CLI (authenticated)
- GitHub CLI (`gh`, authenticated)
- Git

## Safety Mechanisms

- Protected branch detection (main, develop, master)
- Uses `git push --force-with-lease` (safer than force push)
- Converts PR to draft after addressing reviews
- Claude restricted to: git commands, Read, and Edit tools only
