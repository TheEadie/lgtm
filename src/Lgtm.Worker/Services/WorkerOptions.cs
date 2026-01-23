namespace Lgtm.Worker.Services;

public class WorkerOptions
{
    public const string SectionName = "Worker";

    public int IntervalMinutes { get; set; } = 10;
    public string WorkspaceDirectory { get; set; } = "~/lgtm";
}

public class LgtmConfig
{
    public List<string> PullRequestUrls { get; set; } = [];
    public List<string> RepositoryUrls { get; set; } = [];
    public string? GitHubUsername { get; set; }
    public string? NtfyUrl { get; set; }
}
