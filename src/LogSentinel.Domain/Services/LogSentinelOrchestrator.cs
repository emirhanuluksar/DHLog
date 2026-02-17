using LogSentinel.Domain.Abstractions;
using LogSentinel.Domain.Entities;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace LogSentinel.Domain.Services;

public interface ILogSentinelOrchestrator
{
    Task ProcessLogAsync(LogEntry logEntry, CancellationToken cancellationToken = default);
}

public class LogSentinelOrchestrator : ILogSentinelOrchestrator
{
    private readonly ILogAnalyzer _analyzer;
    private readonly IAlertDispatcher _dispatcher;
    private readonly ILogger<LogSentinelOrchestrator> _logger;

    // Thread-safe dictionary for debouncing state.
    // TODO: Migrate to Distributed Cache (Redis) for horizontal scaling support.

    private static readonly ConcurrentDictionary<string, DateTime> _lastAlertTime = new();
    private static readonly TimeSpan _debounceInterval = TimeSpan.FromMinutes(10);

    public LogSentinelOrchestrator(
        ILogAnalyzer analyzer,
        IAlertDispatcher dispatcher,
        ILogger<LogSentinelOrchestrator> logger)
    {
        _analyzer = analyzer;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task ProcessLogAsync(LogEntry logEntry, CancellationToken cancellationToken = default)
    {
        if (logEntry.Level != "Error" && logEntry.Level != "Fatal")
        {
            // Filter out non-actionable log levels to optimize resource utilization

            return;
        }

        var fingerprint = logEntry.CreateFingerprint();
        if (ShouldDebounce(fingerprint))
        {
            _logger.LogInformation("Debouncing duplicate error: {Fingerprint}", fingerprint);
            return;
        }

        try
        {
            _logger.LogInformation("Starting AI Analysis for error: {Message}", logEntry.Message);
            
            // 1. Perform Root Cause Analysis via AI Provider
            var analysis = await _analyzer.AnalyzeAsync(logEntry, cancellationToken);

            // 2. Broadcast analysis results to configured channels
            await _dispatcher.SendAlertAsync(logEntry, analysis, cancellationToken);
            
            // 3. Update debouncing state

            _lastAlertTime[fingerprint] = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process log entry via AI pipeline.");
        }
    }

    private bool ShouldDebounce(string fingerprint)
    {
        if (_lastAlertTime.TryGetValue(fingerprint, out var lastTime))
        {
            if (DateTime.UtcNow - lastTime < _debounceInterval)
            {
                return true;
            }
        }
        return false;
    }
}
