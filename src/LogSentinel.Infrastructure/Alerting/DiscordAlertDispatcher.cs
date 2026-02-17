using LogSentinel.Domain.Abstractions;
using LogSentinel.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using LogSentinel.Infrastructure.Constants;

namespace LogSentinel.Infrastructure.Alerting;

public class DiscordAlertDispatcher : IAlertDispatcher
{
    private readonly HttpClient _httpClient;
    private readonly string _webhookUrl;
    private readonly ILogger<DiscordAlertDispatcher> _logger;

    public DiscordAlertDispatcher(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<DiscordAlertDispatcher> logger)
    {
        _httpClient = httpClientFactory.CreateClient("DiscordWebHook");
        _webhookUrl = configuration[LogSentinelConstants.Alerts.DiscordWebhookSection] ?? "";

        _logger = logger;
    }

    public async Task SendAlertAsync(LogEntry logEntry, AnalysisResult analysis, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_webhookUrl))
        {
            _logger.LogWarning("Discord Webhook URL is not configured. Skipping alert.");
            return;
        }

        var embed = new
        {
            title = $"🚨 {logEntry.Level}: {logEntry.Message[..Math.Min(logEntry.Message.Length, 150)]}...",
            description = $"**Risk Level:** {analysis.RiskLevel}\n**Root Cause:** {analysis.RootCause}\n\n**Suggested Fix:**\n```sql\n{analysis.SuggestedFix}\n```",
            color = analysis.RiskLevel == "High" ? 15158332 : 3447003, // Visual indicator for risk severity

            fields = new[]
            {
                new { name = "Source", value = logEntry.Source, inline = true },
                new { name = "Timestamp", value = logEntry.Timestamp.ToString("u"), inline = true }
            }
        };

        var payload = new
        {
            username = "LogSentinel.AI",
            embeds = new[] { embed }
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(_webhookUrl, payload, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to send Discord alert: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while sending Discord alert.");
        }
    }
}
