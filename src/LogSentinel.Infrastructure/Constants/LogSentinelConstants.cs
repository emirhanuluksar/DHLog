namespace LogSentinel.Infrastructure.Constants;

public static class LogSentinelConstants
{
    public const string DefaultLogLevel = "Info";
    public const string UnknownSource = "Unknown";
    
    public static class AI
    {
        public const string ProviderSection = "AI:Provider";
        public const string ApiKeySection = "AI:ApiKey";
        public const string ModelIdSection = "AI:ModelId";
        public const string EndpointSection = "AI:Endpoint";
        public const string GeminiProvider = "Gemini";
        public const string OpenAIProvider = "OpenAI";
        public const string OllamaProvider = "Ollama";
        public const string DefaultGeminiModel = "gemini-1.5-pro";
    }

    public static class Alerts
    {
        public const string DiscordWebhookSection = "Alerts:DiscordWebhook";
        public const string SmtpSection = "Alerts:Smtp";
        public const string SmtpToSection = "To";
        public const string SmtpUsernameSection = "Username";
        public const string SmtpPasswordSection = "Password";
        public const string SmtpHostSection = "Host";
        public const string SmtpPortSection = "Port";
        public const string SmtpEnableSslSection = "EnableSsl";
    }

    public static class JsonProperties
    {
        public const string Timestamp = "@t";
        public const string Level = "@l";
        public const string MessageTemplate = "@mt";
        public const string MessageTemplateAlt = "MessageTemplate";
        public const string Exception = "Exception";
        public const string SourceContext = "SourceContext";
    }

    public static class AnalysisProperties
    {
        public const string RootCause = "rootCause";
        public const string SuggestedFix = "suggestedFix";
        public const string RiskLevel = "riskLevel";
        public const string IsDebounced = "isDebounced";
    }
}
