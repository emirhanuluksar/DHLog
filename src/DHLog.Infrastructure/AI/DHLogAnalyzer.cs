using DHLog.Domain.Abstractions;
using DHLog.Domain.Entities;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Text.Json;
using DHLog.Infrastructure.Constants;
using Spectre.Console;
using Microsoft.Extensions.Logging;

#pragma warning disable SKEXP0001, SKEXP0010

namespace DHLog.Infrastructure.AI;

public class SemanticKernelLogAnalyzer : ILogAnalyzer
{
    private readonly Kernel _kernel;
    private readonly ILogger<SemanticKernelLogAnalyzer> _logger;

    // Structured system prompt to enforce strict JSON output schema

    private const string SystemPrompt = $@"
You are `DHLog`, an advanced AIOps engineer and expert debugger for .NET applications.
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
    ""{DHLogConstants.AnalysisProperties.RootCause}"": ""string"",
    ""{DHLogConstants.AnalysisProperties.SuggestedFix}"": ""string"",
    ""{DHLogConstants.AnalysisProperties.RiskLevel}"": ""string""
}}

Constraints:
- Be concise but technical.
- If the stack trace implies a database timeout, suggest indexing or query optimization.
- If it's a null reference, pinpoint the likely variable.
";



    public SemanticKernelLogAnalyzer(Kernel kernel, ILogger<SemanticKernelLogAnalyzer> logger)
    {
        _kernel = kernel;
        _logger = logger;
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

        // Invoke AI with spinner
        string jsonResponse = string.Empty;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Aesthetic)
            .StartAsync("[bold cyan]Analyzing Stack Trace (Generative AI)...[/]", async ctx =>
            {
                var result = await _kernel.InvokeAsync(function, new KernelArguments { ["input"] = userPrompt }, cancellationToken);
                jsonResponse = result.GetValue<string>() ?? string.Empty;
            });


        // Deserialize and validate the AI response

        try
        {
            // Remove potential formatting artifacts from the raw response string


            // Improved JSON extraction: ensure we parse only the JSON object
            var cleanJson = jsonResponse.Replace("```json", "").Replace("```", "").Trim();

            // Extract JSON object if wrapped in text
            int firstBrace = cleanJson.IndexOf('{');
            int lastBrace = cleanJson.LastIndexOf('}');

            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                cleanJson = cleanJson.Substring(firstBrace, lastBrace - firstBrace + 1);
            }

            var doc = JsonDocument.Parse(cleanJson);
            var root = doc.RootElement;

            var analysisResult = new AnalysisResult(
                RootCause: root.GetProperty(DHLogConstants.AnalysisProperties.RootCause).GetString() ?? DHLogConstants.UnknownSource,
                SuggestedFix: root.GetProperty(DHLogConstants.AnalysisProperties.SuggestedFix).GetString() ?? "Check logs manually.",
                RiskLevel: root.GetProperty(DHLogConstants.AnalysisProperties.RiskLevel).GetString() ?? DHLogConstants.UnknownSource,
                IsDebounced: false
            );

            // Visual Report
            var table = new Table().Border(TableBorder.Rounded).Expand();
            table.AddColumn(new TableColumn("[bold]Metric[/]").Centered());
            table.AddColumn(new TableColumn("[bold]AI Analysis Result[/]"));

            table.AddRow("[bold cyan]Root Cause[/]", $"[red]{Markup.Escape(analysisResult.RootCause)}[/]");
            table.AddRow("[bold cyan]Suggested Fix[/]", $"[yellow]{Markup.Escape(analysisResult.SuggestedFix)}[/]");
            table.AddRow("[bold cyan]Risk Level[/]", $"[bold magenta]{Markup.Escape(analysisResult.RiskLevel)}[/]");

            AnsiConsole.Write(new Panel(table)
                .Header("[bold red]🚨 CRITICAL INCIDENT REPORT[/]")
                .BorderColor(Color.Orange1).Padding(1, 1, 1, 1));


            return analysisResult;


        }
        catch (Exception ex)
        {
            // Log the failure to standard logs with full context
            _logger.LogError(ex, "Failed to parse AI response. Raw output: {RawResponse}", jsonResponse);

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
