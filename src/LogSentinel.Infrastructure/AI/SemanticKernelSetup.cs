using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using LogSentinel.Domain.Abstractions;
using LogSentinel.Infrastructure.Constants;

#pragma warning disable SKEXP0001, SKEXP0010, SKEXP0070

namespace LogSentinel.Infrastructure.AI;

public static class SemanticKernelSetup
{
    public static IServiceCollection AddLogSentinelAI(this IServiceCollection services, IConfiguration configuration)
    {
        var aiSection = configuration.GetSection("AI");
        var provider = aiSection["Provider"] ?? LogSentinelConstants.AI.GeminiProvider;

        var builder = Kernel.CreateBuilder();

        // Provider Strategy Pattern implementation
        // This architecture enables plug-and-play support for different LLM backends
        if (string.Equals(provider, LogSentinelConstants.AI.GeminiProvider, StringComparison.OrdinalIgnoreCase))
        {
            ConfigureGemini(builder, aiSection);
        }
        else if (string.Equals(provider, LogSentinelConstants.AI.OpenAIProvider, StringComparison.OrdinalIgnoreCase))
        {
            // Requires Microsoft.SemanticKernel.Connectors.OpenAI dependency
            throw new NotSupportedException("OpenAI provider is not currently registered in the DI container.");
        }
        else if (string.Equals(provider, LogSentinelConstants.AI.OllamaProvider, StringComparison.OrdinalIgnoreCase))
        {
            // Requires local inferencing endpoint configuration
            throw new NotSupportedException("Ollama provider requires local endpoint configuration.");
        }
        else
        {
            throw new InvalidOperationException($"The AI Provider '{provider}' is not supported by the current build configuration.");
        }

        services.AddTransient<Kernel>(sp => builder.Build());
        services.AddScoped<ILogAnalyzer, SemanticKernelLogAnalyzer>();

        return services;
    }

    private static void ConfigureGemini(IKernelBuilder builder, IConfigurationSection config)
    {
        var modelId = config["ModelId"] ?? LogSentinelConstants.AI.DefaultGeminiModel;
        var apiKey = config["ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Gemini API Key is missing. Ensure 'AI:ApiKey' is configured in appsettings.json.");
        }

        builder.AddGoogleAIGeminiChatCompletion(
            modelId: modelId,
            apiKey: apiKey
        );
    }
}
