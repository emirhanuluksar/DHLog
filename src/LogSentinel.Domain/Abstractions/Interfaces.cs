using LogSentinel.Domain.Entities;

namespace LogSentinel.Domain.Abstractions;

public interface ILogAnalyzer
{
    Task<AnalysisResult> AnalyzeAsync(LogEntry logEntry, CancellationToken cancellationToken);
}

public interface IAlertDispatcher
{
    Task SendAlertAsync(LogEntry logEntry, AnalysisResult analysis, CancellationToken cancellationToken);
}
