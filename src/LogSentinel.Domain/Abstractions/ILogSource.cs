using LogSentinel.Domain.Entities;

namespace LogSentinel.Domain.Abstractions;

public interface ILogSource
{
    IAsyncEnumerable<LogEntry> StreamLogsAsync(CancellationToken cancellationToken);
}
