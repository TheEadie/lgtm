namespace Lgtm.Worker.Services;

/// <summary>
/// Resolves the list of PR URLs to process, combining explicit URLs and repository-discovered PRs.
/// </summary>
public interface IPrUrlResolver
{
    /// <summary>
    /// Gets all PR URLs to process, dynamically resolving repository PRs.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A deduplicated list of PR URLs to process.</returns>
    Task<List<string>> GetPrUrlsAsync(CancellationToken cancellationToken);
}
