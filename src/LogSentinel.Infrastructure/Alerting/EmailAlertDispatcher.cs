using LogSentinel.Domain.Abstractions;
using LogSentinel.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Mail;
using System.Text;
using LogSentinel.Infrastructure.Constants;

namespace LogSentinel.Infrastructure.Alerting;

public class EmailAlertDispatcher : IAlertDispatcher
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailAlertDispatcher> _logger;

    public EmailAlertDispatcher(IConfiguration configuration, ILogger<EmailAlertDispatcher> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendAlertAsync(LogEntry logEntry, AnalysisResult analysis, CancellationToken cancellationToken)
    {
        var smtpSettings = _configuration.GetSection(LogSentinelConstants.Alerts.SmtpSection);

        if (!smtpSettings.Exists())
        {
             _logger.LogWarning("Email sending skipped. 'Alerts:Smtp' not configured.");
             return;
        }

        string toAddress = smtpSettings[LogSentinelConstants.Alerts.SmtpToSection] ?? "";

        if (string.IsNullOrEmpty(toAddress))
        {
             _logger.LogWarning("Email sending skipped. 'Alerts:Smtp:To' is empty.");
             return;
        }

        try
        {
            var senderEmail = smtpSettings[LogSentinelConstants.Alerts.SmtpUsernameSection];
            var password = smtpSettings[LogSentinelConstants.Alerts.SmtpPasswordSection];
            var host = smtpSettings[LogSentinelConstants.Alerts.SmtpHostSection];
            var port = int.Parse(smtpSettings[LogSentinelConstants.Alerts.SmtpPortSection] ?? "587");
            var enableSsl = bool.Parse(smtpSettings[LogSentinelConstants.Alerts.SmtpEnableSslSection] ?? "true");


            using var client = new SmtpClient(host, port)
            {
                Credentials = new NetworkCredential(senderEmail, password),
                EnableSsl = enableSsl
            };

            var mailMessage = new MailMessage
            {
                From = new MailAddress(senderEmail!, "LogSentinel AI"),
                Subject = $"🚨 AI Alert: {logEntry.Level} in {logEntry.Source}",
                Body = BuildHtmlBody(logEntry, analysis),
                IsBodyHtml = true
            };
            mailMessage.To.Add(toAddress);

            await client.SendMailAsync(mailMessage, cancellationToken);
            _logger.LogInformation("Email alert sent to {Email}", toAddress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email alert.");
        }
    }

    private string BuildHtmlBody(LogEntry log, AnalysisResult analysis)
    {
        var color = analysis.RiskLevel == "Critical" || analysis.RiskLevel == "High" ? "#e74c3c" : "#f1c40f";

        return $@"
<html>
<body style='font-family: Arial, sans-serif;'>
    <div style='border-left: 5px solid {color}; padding: 15px; background: #f9f9f9;'>
        <h2 style='color: {color}; margin-top:0;'>⚠️ {log.Level}: {log.Message}</h2>
        <p><strong>Source:</strong> {log.Source} | <strong>Time:</strong> {log.Timestamp:u}</p>
        
        <h3>🤖 AI Root Cause Analysis</h3>
        <p>{analysis.RootCause}</p>
        
        <h3>🛠️ Suggested Fix</h3>
        <pre style='background: #2d2d2d; color: #ecf0f1; padding: 10px; border-radius: 5px; overflow-x: auto;'>
{analysis.SuggestedFix}
        </pre>
        
        <p><em>Risk Level: <strong>{analysis.RiskLevel}</strong></em></p>
    </div>
</body>
</html>";
    }
}
