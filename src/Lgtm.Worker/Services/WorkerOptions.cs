namespace Lgtm.Worker.Services;

public class WorkerOptions
{
    public const string SectionName = "Worker";

    public int IntervalMinutes { get; set; } = 10;
    public List<RepositoryConfig> Repositories { get; set; } = [];
}

public class RepositoryConfig
{
    public string Path { get; set; } = string.Empty;
    public string PullRequestUrl { get; set; } = string.Empty;
}
