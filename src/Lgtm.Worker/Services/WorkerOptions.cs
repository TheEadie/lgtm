namespace Lgtm.Worker.Services;

public class WorkerOptions
{
    public const string SectionName = "Worker";

    public int IntervalMinutes { get; set; } = 10;
    public string WeatherPrompt { get; set; } = "Get the current weather in Cambridge, UK and provide a brief summary.";
}
