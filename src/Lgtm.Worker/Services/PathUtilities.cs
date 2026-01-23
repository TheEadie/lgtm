using System.Text.RegularExpressions;
using Lgtm.Worker.Models;

namespace Lgtm.Worker.Services;

/// <summary>
/// Provides utility methods for path manipulation and GitHub PR URL parsing.
/// </summary>
public static class PathUtilities
{
    private static readonly HashSet<string> ProtectedBranches = ["main", "develop", "master"];

    private static readonly Regex PrUrlRegex = new(
        @"^https://github\.com/(?<owner>[^/]+)/(?<repo>[^/]+)/pull/(?<number>\d+)",
        RegexOptions.Compiled);

    private static readonly Regex RepoUrlRegex = new(
        @"^https://github\.com/(?<owner>[^/]+)/(?<repo>[^/]+)/?$",
        RegexOptions.Compiled);

    /// <summary>
    /// Expands a path that starts with '~' to use the user's home directory.
    /// </summary>
    /// <param name="path">The path to expand.</param>
    /// <returns>The expanded path, or the original path if it doesn't start with '~'.</returns>
    public static string ExpandPath(string path)
    {
        if (path.StartsWith('~'))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, path[1..].TrimStart('/'));
        }
        return path;
    }

    /// <summary>
    /// Parses a GitHub Pull Request URL to extract the owner, repository, and PR number.
    /// </summary>
    /// <param name="url">The GitHub PR URL to parse (e.g., "https://github.com/owner/repo/pull/123").</param>
    /// <returns>A PrInfo record if the URL is valid, null otherwise.</returns>
    public static PrInfo? ParsePrUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var match = PrUrlRegex.Match(url);
        if (!match.Success)
            return null;

        var owner = match.Groups["owner"].Value;
        var repo = match.Groups["repo"].Value;
        var number = int.Parse(match.Groups["number"].Value);

        return new PrInfo(owner, repo, number);
    }

    /// <summary>
    /// Determines whether a branch name is protected (main, develop, or master).
    /// </summary>
    /// <param name="branchName">The branch name to check.</param>
    /// <returns>True if the branch is protected, false otherwise.</returns>
    public static bool IsProtectedBranch(string branchName)
    {
        return ProtectedBranches.Contains(branchName.ToLowerInvariant());
    }

    /// <summary>
    /// Parses a GitHub repository URL to extract the owner and repository name.
    /// </summary>
    /// <param name="url">The GitHub repository URL to parse (e.g., "https://github.com/owner/repo").</param>
    /// <returns>A tuple of (owner, repo) if the URL is valid, null otherwise.</returns>
    public static (string Owner, string Repo)? ParseRepoUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var match = RepoUrlRegex.Match(url);
        if (!match.Success)
            return null;

        var owner = match.Groups["owner"].Value;
        var repo = match.Groups["repo"].Value;

        return (owner, repo);
    }
}
