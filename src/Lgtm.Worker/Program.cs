using System.Text.Json;
using Lgtm.Worker.Services;

if (args.Length == 0)
{
    Console.WriteLine("Usage: Lgtm.Worker <config-file>");
    Console.WriteLine("  config-file: Path to JSON file containing repository configuration");
    return 1;
}

var configPath = args[0];
if (!File.Exists(configPath))
{
    Console.WriteLine($"Config file not found: {configPath}");
    return 1;
}

List<RepositoryConfig> repositories;
try
{
    var json = await File.ReadAllTextAsync(configPath);
    repositories = JsonSerializer.Deserialize<List<RepositoryConfig>>(json, new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    }) ?? [];
}
catch (JsonException ex)
{
    Console.WriteLine($"Failed to parse config file: {ex.Message}");
    return 1;
}

Console.WriteLine($"Loaded {repositories.Count} repositories from {configPath}");

var builder = Host.CreateApplicationBuilder(args.Skip(1).ToArray());

// Disable framework logging (using Console.WriteLine instead)
builder.Logging.ClearProviders();

// Configure options
builder.Services.Configure<WorkerOptions>(
    builder.Configuration.GetSection(WorkerOptions.SectionName));
builder.Services.AddSingleton(repositories);

// Register services
builder.Services.AddSingleton<IWorkProcessor, WorkProcessor>();
builder.Services.AddHostedService<ScheduledWorkerService>();

var host = builder.Build();
host.Run();

return 0;
