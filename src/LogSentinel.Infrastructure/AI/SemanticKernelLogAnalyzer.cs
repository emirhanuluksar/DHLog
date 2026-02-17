using LogSentinel.Domain.Abstractions;
using LogSentinel.Domain.Entities;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Text.Json;
using LogSentinel.Infrastructure.Constants;

#pragma warning disable SKEXP0001, SKEXP0010

namespace LogSentinel.Infrastructure.AI;

public class SemanticKernelLogAnalyzer : ILogAnalyzer
{
    private readonly Kernel _kernel;

    // Structured system prompt to enforce strict JSON output schema

    private const string SystemPrompt = $@"
You are `LogSentinel`, an advanced AIOps engineer and expert debugger for .NET applications.
Your job is to analyze application logs to identify the root cause and suggest fixes.

Input:
A log entry containing: Source, Level, Message, and StackTrace.

Task:
1. Analyze the stack trace and error message.
2. Determine the most likely Root Cause (Why did this happen?).
3. Suggest a concrete Fix (Code snippet, SQL optimization, or configuration change).
4. Assess the Risk Level (Low, Medium, High, Critical).

Output Format:
You MUST output raw JSON only. Do not include markdown formatting (```json ... ```).
The JSON structure must be exactly:
{{
    ""{{LogSentinelConstants.AnalysisProperties.RootCause}}"": ""string"",
    ""{{LogSentinelConstants.AnalysisProperties.SuggestedFix}}"": ""string"",
    ""{{LogSentinelConstants.AnalysisProperties.RiskLevel}}"": ""string""
}}

Constraints:
- Be concise but technical.
- If the stack trace implies a database timeout, suggest indexing or query optimization.
- If it's a null reference, pinpoint the likely variable.
";


    public SemanticKernelLogAnalyzer(Kernel kernel)
    {
        _kernel = kernel;
    }

    public async Task<AnalysisResult> AnalyzeAsync(LogEntry logEntry, CancellationToken cancellationToken)
    {
        var userPrompt = $@"
Error Level: {logEntry.Level}
Source: {logEntry.Source}
Message: {logEntry.Message}
Stack Trace:
{logEntry.StackTrace}
";

        var settings = new OpenAIPromptExecutionSettings
        {
            Temperature = 0.2, // Ensure deterministic output generation


            MaxTokens = 500,
            ResponseFormat = "json_object" // Enforce strictly valid JSON output

        };
        
        // Instantiate the semantic function

        var function = _kernel.CreateFunctionFromPrompt(
            promptTemplate: SystemPrompt + "\nUser Input:\n{{$input}}",
            executionSettings: settings
        );

        var result = await _kernel.InvokeAsync(function, new KernelArguments { ["input"] = userPrompt }, cancellationToken);
        var jsonResponse = result.GetValue<string>() ?? string.Empty;

        // Deserialize and validate the AI response

        try
        {
            // Remove potential formatting artifacts from the raw response string


            var cleanJson = jsonResponse.Replace("```json", "").Replace("```", "").Trim();
            
            var doc = JsonDocument.Parse(cleanJson);
            var root = doc.RootElement;
            
            return new AnalysisResult(
                RootCause: root.GetProperty(LogSentinelConstants.AnalysisProperties.RootCause).GetString() ?? LogSentinelConstants.UnknownSource,
                SuggestedFix: root.GetProperty(LogSentinelConstants.AnalysisProperties.SuggestedFix).GetString() ?? "Check logs manually.",
                RiskLevel: root.GetProperty(LogSentinelConstants.AnalysisProperties.RiskLevel).GetString() ?? LogSentinelConstants.UnknownSource,
                IsDebounced: false
            );

        }
        catch (Exception)
        {
            // Handle deserialization failures gracefully


            return new AnalysisResult(
                RootCause: "AI Analysis Failed to Parse",
                SuggestedFix: "Manual investigation required. Raw AI Output: " + jsonResponse,
                RiskLevel: "Medium",
                IsDebounced: false
            );
        }
    }
}
