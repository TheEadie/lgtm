using System.Text.Json;
using Lgtm.Worker.Services;

var configPath = args.Length > 0
    ? args[0]
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "lgtm", "config.json");

if (!File.Exists(configPath))
{
    Console.WriteLine($"Config file not found: {configPath}");
    if (args.Length == 0)
    {
        Console.WriteLine("Usage: Lgtm.Worker [config-file]");
        Console.WriteLine("  config-file: Path to JSON file containing pull request URLs (default: ~/lgtm/config.json)");
    }
    return 1;
}

List<string> pullRequestUrls;
try
{
    var json = await File.ReadAllTextAsync(configPath);
    var config = JsonSerializer.Deserialize<LgtmConfig>(json, new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    });
    pullRequestUrls = config?.PullRequestUrls ?? [];
}
catch (JsonException ex)
{
    Console.WriteLine($"Failed to parse config file: {ex.Message}");
    return 1;
}

Console.WriteLine($"Loaded {pullRequestUrls.Count} pull request(s) from {configPath}");

var builder = Host.CreateApplicationBuilder(args.Skip(1).ToArray());

// Disable framework logging (using Console.WriteLine instead)
builder.Logging.ClearProviders();

// Configure options
builder.Services.Configure<WorkerOptions>(
    builder.Configuration.GetSection(WorkerOptions.SectionName));
builder.Services.AddSingleton(pullRequestUrls);

// Register services
builder.Services.AddSingleton<IGitHubClient, GitHubClient>();
builder.Services.AddSingleton<IClaudeInteractor, ClaudeInteractor>();
builder.Services.AddSingleton<IResolutionPromptBuilder, ResolutionPromptBuilder>();
builder.Services.AddSingleton<IPrStateTracker, PrStateTracker>();
builder.Services.AddSingleton<IWorkProcessor, WorkProcessor>();
builder.Services.AddHostedService<ScheduledWorkerService>();

var host = builder.Build();
host.Run();

return 0;
