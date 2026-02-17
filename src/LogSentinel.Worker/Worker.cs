using LogSentinel.Domain.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LogSentinel.Worker;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly ILogSource _logSource;
    private readonly IServiceScopeFactory _scopeFactory;

    public Worker(
        ILogSource logSource,
        IServiceScopeFactory scopeFactory,
        ILogger<Worker> logger)
    {
        _logSource = logSource;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LogSentinel.AI Worker starting at: {time}", DateTimeOffset.Now);

        try
        {
            // Begin continuous log stream monitoring

            await foreach (var logEntry in _logSource.StreamLogsAsync(stoppingToken))
            {
                if (stoppingToken.IsCancellationRequested) break;

                // Establish a new scope for discrete processing units
                // This ensures correct lifecycle management for scoped services like the AI Kernel

                using (var scope = _scopeFactory.CreateScope())
                {
                    var orchestrator = scope.ServiceProvider.GetRequiredService<LogSentinel.Domain.Services.ILogSentinelOrchestrator>();
                    
                    try 
                    {
                        await orchestrator.ProcessLogAsync(logEntry, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing log entry: {LogEntry}", logEntry);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Fatal error in LogSentinel Worker loop.");
        }
    }
}
