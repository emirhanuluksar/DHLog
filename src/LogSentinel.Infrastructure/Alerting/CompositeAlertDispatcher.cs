using LogSentinel.Domain.Abstractions;
using LogSentinel.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace LogSentinel.Infrastructure.Alerting;

public class CompositeAlertDispatcher : IAlertDispatcher
{
    private readonly IEnumerable<IAlertDispatcher> _dispatchers;
    private readonly ILogger<CompositeAlertDispatcher> _logger;

    public CompositeAlertDispatcher(IEnumerable<IAlertDispatcher> dispatchers, ILogger<CompositeAlertDispatcher> logger)
    {
        _dispatchers = dispatchers;
        _logger = logger;
    }

    public async Task SendAlertAsync(LogEntry logEntry, AnalysisResult analysis, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Broadcasting alert to {Count} channels...", _dispatchers.Count());

        foreach (var dispatcher in _dispatchers)
        {
            // Prevent recursive dispatch loops
            if (dispatcher is CompositeAlertDispatcher) continue;


            try
            {
                await dispatcher.SendAlertAsync(logEntry, analysis, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to dispatch alert via {DispatcherType}", dispatcher.GetType().Name);
            }
        }
    }
}
