using System;

namespace LogSentinel.Domain.Entities;

public record LogEntry(
    string Source,
    string Level,
    string Message,
    string StackTrace,
    DateTime Timestamp
)
{
    // Generates a deterministic hash for deduplication logic
    public string CreateFingerprint()
    {

        return $"{Source}:{Level}:{Message}:{StackTrace ?? ""}".GetHashCode().ToString();
    }
}

public record AnalysisResult(
    string RootCause,
    string SuggestedFix,
    string RiskLevel,
    bool IsDebounced = false
);
