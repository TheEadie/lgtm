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

LgtmConfig config;
try
{
    var json = await File.ReadAllTextAsync(configPath);
    config = JsonSerializer.Deserialize<LgtmConfig>(json, new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    }) ?? new LgtmConfig();
}
catch (JsonException ex)
{
    Console.WriteLine($"Failed to parse config file: {ex.Message}");
    return 1;
}

Console.WriteLine($"Loaded config from {configPath}");
Console.WriteLine($"  - {config.PullRequestUrls.Count} explicit PR URL(s)");
Console.WriteLine($"  - {config.RepositoryUrls.Count} repository URL(s) to monitor");
if (!string.IsNullOrWhiteSpace(config.GitHubUsername))
{
    Console.WriteLine($"  - Filtering PRs by author: {config.GitHubUsername}");
}

var builder = Host.CreateApplicationBuilder(args.Skip(1).ToArray());

// Disable framework logging (using Console.WriteLine instead)
builder.Logging.ClearProviders();

// Configure options
builder.Services.Configure<WorkerOptions>(
    builder.Configuration.GetSection(WorkerOptions.SectionName));
builder.Services.AddSingleton(config);

// Register services
builder.Services.AddSingleton<IGitHubClient, GitHubClient>();
builder.Services.AddSingleton<IClaudeInteractor, ClaudeInteractor>();
builder.Services.AddSingleton<IResolutionPromptBuilder, ResolutionPromptBuilder>();
builder.Services.AddSingleton<IPrStateTracker, PrStateTracker>();
builder.Services.AddSingleton<IPrUrlResolver, PrUrlResolver>();
builder.Services.AddSingleton<ILessonExtractor, LessonExtractor>();
builder.Services.AddSingleton<ILessonStore, LessonStore>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<INotificationService>(sp =>
    new NtfyNotificationService(sp.GetRequiredService<IHttpClientFactory>().CreateClient(), config.NtfyUrl));
builder.Services.AddSingleton<IWorkProcessor, WorkProcessor>();
builder.Services.AddHostedService<ScheduledWorkerService>();

var host = builder.Build();
host.Run();

return 0;
