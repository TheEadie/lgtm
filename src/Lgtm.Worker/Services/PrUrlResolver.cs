namespace Lgtm.Worker.Services;

/// <summary>
/// Resolves PR URLs by combining explicit URLs from config with dynamically discovered PRs from repositories,
/// plus any PRs currently being tracked (to catch their merge notifications).
/// </summary>
public class PrUrlResolver : IPrUrlResolver
{
    private readonly LgtmConfig _config;
    private readonly IGitHubClient _gitHubClient;
    private readonly IPrStateTracker _stateTracker;

    public PrUrlResolver(LgtmConfig config, IGitHubClient gitHubClient, IPrStateTracker stateTracker)
    {
        _config = config;
        _gitHubClient = gitHubClient;
        _stateTracker = stateTracker;
    }

    /// <inheritdoc/>
    public async Task<List<string>> GetPrUrlsAsync(CancellationToken cancellationToken)
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Add explicit PR URLs from config
        foreach (var url in _config.PullRequestUrls)
        {
            urls.Add(url);
        }

        // Discover PRs from configured repositories
        if (!string.IsNullOrWhiteSpace(_config.GitHubUsername) && _config.RepositoryUrls.Count > 0)
        {
            foreach (var repoUrl in _config.RepositoryUrls)
            {
                var repoInfo = PathUtilities.ParseRepoUrl(repoUrl);
                if (repoInfo is null)
                {
                    Console.WriteLine($"Invalid repository URL: {repoUrl}");
                    continue;
                }

                var (owner, repo) = repoInfo.Value;
                var prs = await _gitHubClient.GetOpenPrsByAuthorAsync(owner, repo, _config.GitHubUsername, cancellationToken);

                foreach (var pr in prs)
                {
                    urls.Add(pr.Url);
                }

                if (prs.Count > 0)
                {
                    Console.WriteLine($"Discovered {prs.Count} open PR(s) in {owner}/{repo}");
                }
            }
        }

        // Include PRs we're already tracking (to catch their merge notifications)
        // This ensures we see when a tracked PR gets merged, even though it's no longer "open"
        foreach (var trackedUrl in _stateTracker.GetTrackedPrUrls())
        {
            urls.Add(trackedUrl);
        }

        return urls.ToList();
    }
}
