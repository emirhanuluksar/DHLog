using LogSentinel.Domain.Abstractions;
using LogSentinel.Domain.Services;
using LogSentinel.Infrastructure.AI;
using LogSentinel.Infrastructure.Alerting;
using LogSentinel.Infrastructure.Inputs;
using LogSentinel.Worker;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// Load configuration from appsettings and environment variables (Docker/K8s support)
builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();


// Domain Services
builder.Services.AddScoped<ILogSentinelOrchestrator, LogSentinelOrchestrator>();

// Infrastructure - AI
builder.Services.AddLogSentinelAI(builder.Configuration);

// Infrastructure - Alerting
builder.Services.AddHttpClient("DiscordWebHook");

// Register Dispatchers
// Note: Registered as concrete types to allow composition in CompositeAlertDispatcher
builder.Services.AddTransient<DiscordAlertDispatcher>();

builder.Services.AddTransient<EmailAlertDispatcher>();

// Register the Composite as the main implementation
builder.Services.AddTransient<IAlertDispatcher>(sp => 
{
    var logger = sp.GetRequiredService<ILogger<CompositeAlertDispatcher>>();
    var discord = sp.GetRequiredService<DiscordAlertDispatcher>();
    var email = sp.GetRequiredService<EmailAlertDispatcher>();

    return new CompositeAlertDispatcher(new IAlertDispatcher[] { discord, email }, logger);
});

// Configure Log Input Source
// Default: Local file watcher. In distributed environments, replace with centralized log aggregator (e.g., Seq, ELK).
string logPath = Path.Combine(Directory.GetCurrentDirectory(), "app_logs.txt");

builder.Services.AddSingleton<ILogSource>(sp => 
    new FileLogWatcher(logPath, sp.GetRequiredService<ILogger<FileLogWatcher>>()));

// Worker Service
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
