using LogSentinel.Domain.Abstractions;
using LogSentinel.Domain.Entities;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using LogSentinel.Infrastructure.Constants;

namespace LogSentinel.Infrastructure.Inputs;

public class FileLogWatcher : ILogSource
{
    private readonly string _logFilePath;
    private readonly ILogger<FileLogWatcher> _logger;

    public FileLogWatcher(string logFilePath, ILogger<FileLogWatcher> logger)
    {
        _logFilePath = logFilePath;
        _logger = logger;
    }

    public async IAsyncEnumerable<LogEntry> StreamLogsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _logger.LogInformation("Watching file: {Path}", _logFilePath);

        if (!File.Exists(_logFilePath))
        {
            _logger.LogWarning("File not found, creating dummy file: {Path}", _logFilePath);
            await File.WriteAllTextAsync(_logFilePath, "", cancellationToken);
        }

        using var fileStream = new FileStream(_logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fileStream);

        // Seek to end to start with new logs
        fileStream.Seek(0, SeekOrigin.End);

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync();
            if (line != null)
            {
                // Attempt to parse log line based on expected format strategy
                LogEntry? entry = ParseLogLine(line);

                if (entry != null)
                {
                    yield return entry;
                }
            }
            else
            {
                // Wait for new content
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    private LogEntry? ParseLogLine(string line)
    {
        // Primary Strategy: Structured JSON (Serilog Compact Format)
        if (line.TrimStart().StartsWith("{"))

        {
            try
            {
                var doc = System.Text.Json.JsonDocument.Parse(line);
                var root = doc.RootElement;

                string timestampStr = root.TryGetProperty(LogSentinelConstants.JsonProperties.Timestamp, out var t) ? t.GetString() ?? "" : "";

                string level = root.TryGetProperty(LogSentinelConstants.JsonProperties.Level, out var l) ? l.GetString() ?? LogSentinelConstants.DefaultLogLevel : LogSentinelConstants.DefaultLogLevel;

                string message = root.TryGetProperty(LogSentinelConstants.JsonProperties.MessageTemplate, out var mt) ? mt.GetString() ?? "" : 
                                 (root.TryGetProperty(LogSentinelConstants.JsonProperties.MessageTemplateAlt, out var mt2) ? mt2.GetString() ?? "" : "");

                string exception = root.TryGetProperty(LogSentinelConstants.JsonProperties.Exception, out var ex) ? ex.GetString() ?? "" : "";

                string source = root.TryGetProperty(LogSentinelConstants.JsonProperties.SourceContext, out var sc) ? sc.GetString() ?? LogSentinelConstants.UnknownSource : LogSentinelConstants.UnknownSource;


                if (!string.IsNullOrEmpty(exception) || level == "Error" || level == "Fatal")
                {
                    return new LogEntry(
                        Source: source,
                        Level: level,
                        Message: message,
                        StackTrace: exception,
                        Timestamp: DateTime.TryParse(timestampStr, out var dt) ? dt : DateTime.UtcNow
                    );
                }
            }
            catch 
            {
                // JSON parsing failure is expected for non-JSON lines; proceed to fallback strategy
            }
        }

        // Fallback Strategy: Legacy pipe-delimited format

        var parts = line.Split('|');
        if (parts.Length >= 5)
        {
            return new LogEntry(
                Source: parts[2],
                Level: parts[1],
                Message: parts[3],
                StackTrace: parts[4],
                Timestamp: DateTime.TryParse(parts[0], out var dt) ? dt : DateTime.UtcNow
            );
        }
        
        // Return null for unparseable lines
        return null;
    }
}
