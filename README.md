# LGTM - Automated PR Maintenance

A .NET worker that monitors GitHub pull requests and uses Claude CLI to automatically:
- Clone repositories and checkout PR branches
- Resolve merge conflicts by rebasing on the target branch
- Address new review comments and push fixes

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Claude CLI](https://docs.anthropic.com/en/docs/claude-cli) - authenticated and configured
- [GitHub CLI](https://cli.github.com/) (`gh`) - authenticated with access to your repositories

## Configuration

Create a JSON config file to configure monitoring. By default, the tool looks for `~/lgtm/config.json`.

### Basic Configuration

Monitor specific PRs by URL:

```json
{
    "PullRequestUrls": [
        "https://github.com/owner/repo/pull/123",
        "https://github.com/owner/other-repo/pull/456"
    ]
}
```

### Repository Monitoring

Automatically discover and monitor all your open PRs in specific repositories:

```json
{
    "RepositoryUrls": [
        "https://github.com/owner/repo",
        "https://github.com/owner/other-repo"
    ],
    "GitHubUsername": "your-github-username"
}
```

When `GitHubUsername` is set, the tool will query each repository for open PRs authored by that user. This is useful for monitoring all your PRs without manually adding each URL.

You can combine both approaches:

```json
{
    "PullRequestUrls": [
        "https://github.com/external/repo/pull/789"
    ],
    "RepositoryUrls": [
        "https://github.com/owner/repo"
    ],
    "GitHubUsername": "your-github-username"
}
```

The tool will automatically clone repositories into a workspace directory (default: `~/lgtm`) and checkout the PR branch using `gh pr checkout`.

## Running

```bash
dotnet run --project src/Lgtm.Worker [config-file]
```

If no config file is specified, it defaults to `~/lgtm/config.json`.

Examples:
```bash
# Use default config file (~/lgtm/config.json)
dotnet run --project src/Lgtm.Worker

# Use a custom config file
dotnet run --project src/Lgtm.Worker ./my-config.json
```

The worker will run continuously, checking each PR at a configured interval (default: 10 minutes).

## Processing Logic

For each configured PR, the worker:

1. **Skips merged PRs** - No action needed
2. **Resolves conflicts** - If the PR has merge conflicts, invokes Claude to:
   - Fetch latest changes
   - Rebase on the target branch
   - Resolve conflicts
   - Force push the updated branch
3. **Addresses review comments** - If there are new review comments since the last commit, invokes Claude to:
   - Read and understand the feedback
   - Make necessary code changes
   - Commit and push the fixes

## Notifications (Ntfy)

LGTM can send push notifications via [ntfy](https://ntfy.sh/) when:
- A PR is merged
- Reviews have been addressed (PR converted to draft for your review)

To enable notifications, add your ntfy topic URL to the config:

```json
{
    "PullRequestUrls": ["..."],
    "NtfyUrl": "https://ntfy.sh/your-topic-name"
}
```

You can use the public ntfy.sh server or self-host. Install the ntfy app on your phone to receive notifications.

## Per-Repository Lessons

LGTM learns from PR review feedback and stores lessons per repository in `~/lgtm/lessons/{owner}/{repo}.md`. These lessons are:

- **Automatically extracted** from reviewer comments when addressing reviews
- **Initialized from history** on first run for configured repositories (analyzes the last 10 PRs)
- **Consolidated by Claude** to avoid duplicates and organize by category
- **Included in prompts** when addressing future reviews in the same repository

This helps Claude make consistent improvements aligned with your team's coding standards.

## Configuration Options

The polling interval and workspace directory can be configured via `appsettings.json` (in the project directory):

```json
{
  "Worker": {
    "IntervalMinutes": 10,
    "WorkspaceDirectory": "~/lgtm"
  }
}
```

- `IntervalMinutes`: How often to check PRs (default: 10 minutes)
- `WorkspaceDirectory`: Where to clone repositories (default: `~/lgtm`, supports `~` for home directory)

### Full Config Example

Here's a complete `config.json` showing all available options:

```json
{
    "PullRequestUrls": [
        "https://github.com/owner/repo/pull/123"
    ],
    "RepositoryUrls": [
        "https://github.com/owner/repo",
        "https://github.com/owner/other-repo"
    ],
    "GitHubUsername": "your-github-username",
    "NtfyUrl": "https://ntfy.sh/your-topic-name"
}
```

## Safety

- Protected branches (`main`, `develop`, `master`) cannot be force-pushed
- Uses `--force-with-lease` for safer force pushes during conflict resolution
- Claude CLI is restricted to only `git` commands, `Read`, and `Edit` tools
