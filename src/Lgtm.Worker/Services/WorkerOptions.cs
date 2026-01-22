namespace Lgtm.Worker.Services;

public class WorkerOptions
{
    public const string SectionName = "Worker";

    public int IntervalMinutes { get; set; } = 10;
}
