using LogSentinel.Domain.Abstractions;
using LogSentinel.Domain.Entities;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using LogSentinel.Infrastructure.Constants;

namespace LogSentinel.Infrastructure.Inputs;

public class FileLogWatcher : ILogSource
{
    private readonly string _logFilePath;
    private readonly ILogger<FileLogWatcher> _logger;
    private readonly StringBuilder _currentLogBuffer = new();

    private static readonly Regex _timestampRegex = new(
        @"^\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:?\d{2})?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

        fileStream.Seek(0, SeekOrigin.End);

        DateTime lastLineReceivedUtc = DateTime.UtcNow;

        while (!cancellationToken.IsCancellationRequested)
        {
            string? line = await reader.ReadLineAsync();

            if (line != null)
            {
                lastLineReceivedUtc = DateTime.UtcNow;

                if (IsNewLogStart(line))
                {
                    if (_currentLogBuffer.Length > 0)
                    {
                        var entry = ParseBufferedLog(_currentLogBuffer.ToString());
                        if (entry != null) yield return entry;
                        _currentLogBuffer.Clear();
                    }
                }

                _currentLogBuffer.AppendLine(line);

                if (line.TrimStart().StartsWith("{") && !line.TrimEnd().EndsWith("}"))
                {
                }
            }
            else
            {
                if (_currentLogBuffer.Length > 0 && (DateTime.UtcNow - lastLineReceivedUtc).TotalMilliseconds > 500)
                {
                    var entry = ParseBufferedLog(_currentLogBuffer.ToString());
                    if (entry != null) yield return entry;

                    _currentLogBuffer.Clear();
                }

                await Task.Delay(500, cancellationToken);
            }
        }
    }

    private bool IsNewLogStart(string line)
    {
        return _timestampRegex.IsMatch(line) || line.TrimStart().StartsWith("{");
    }

    private LogEntry? ParseBufferedLog(string logBlock)
    {
        var trimmedBlock = logBlock.TrimStart();

        if (trimmedBlock.StartsWith("{"))
        {
            return ParseJsonLine(trimmedBlock);
        }

        var lines = logBlock.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return null;

        var firstLine = lines[0];
        var match = _timestampRegex.Match(firstLine);

        if (!match.Success) return null;

        if (!DateTime.TryParse(match.Value, out var timestamp))
        {
            timestamp = DateTime.UtcNow;
        }

        string remainder = firstLine.Substring(match.Length).Trim();
        string level = "Info";
        string message = remainder;

        var levelMatch = Regex.Match(remainder, @"^\[([A-Z]+)\]|^([A-Z]{3,5})[\s:]");

        if (levelMatch.Success)
        {
            string rawLevel = levelMatch.Groups[1].Success ? levelMatch.Groups[1].Value : levelMatch.Groups[2].Value;
            level = MapShortLevel(rawLevel);
            message = remainder.Substring(levelMatch.Length).Trim();
        }
        else if (firstLine.Contains('|'))
        {
            return ParsePipeLine(firstLine);
        }

        if (level != "Error" && level != "Fatal")
        {
            return null;
        }

        var stackTraceBuilder = new StringBuilder();
        for (int i = 1; i < lines.Length; i++)
        {
            stackTraceBuilder.AppendLine(lines[i]);
        }
        string stackTrace = stackTraceBuilder.ToString().Trim();

        string source = "Application";
        var exceptionSourceMatch = Regex.Match(message, @"([\w\.]+Exception):");
        if (exceptionSourceMatch.Success)
        {
            source = exceptionSourceMatch.Groups[1].Value;
        }

        return new LogEntry(
            Source: source,
            Level: level,
            Message: message,
            StackTrace: stackTrace,
            Timestamp: timestamp
        );
    }

    private string MapShortLevel(string shortLevel)
    {
        return shortLevel.ToUpperInvariant() switch
        {
            "INF" => "Info",
            "INFO" => "Info",
            "WRN" => "Warning",
            "WARN" => "Warning",
            "ERR" => "Error",
            "ERROR" => "Error",
            "FTL" => "Fatal",
            "FATAL" => "Fatal",
            "DBG" => "Debug",
            "DEBUG" => "Debug",
            _ => "Info"
        };
    }

    private LogEntry? ParseJsonLine(string line)
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
        }
        return null;
    }

    private LogEntry? ParsePipeLine(string line)
    {
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
        return null;
    }
}

