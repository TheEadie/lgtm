using Lgtm.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);

// Configure options
builder.Services.Configure<WorkerOptions>(
    builder.Configuration.GetSection(WorkerOptions.SectionName));

// Register services
builder.Services.AddSingleton<IWorkProcessor, WorkProcessor>();
builder.Services.AddHostedService<ScheduledWorkerService>();

var host = builder.Build();
host.Run();
