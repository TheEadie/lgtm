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

Create a JSON config file with pull request URLs to monitor:

```json
{
    "PullRequestUrls": [
        "https://github.com/owner/repo/pull/123",
        "https://github.com/owner/other-repo/pull/456"
    ]
}
```

The tool will automatically clone repositories into a workspace directory (default: `~/lgtm`) and checkout the PR branch using `gh pr checkout`.

## Running

```bash
dotnet run --project src/Lgtm.Worker <config-file>
```

Example:
```bash
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

## Configuration Options

The polling interval and workspace directory can be configured via `appsettings.json`:

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

## Safety

- Protected branches (`main`, `develop`, `master`) cannot be force-pushed
- Uses `--force-with-lease` for safer force pushes during conflict resolution
- Claude CLI is restricted to only `git` commands, `Read`, and `Edit` tools
