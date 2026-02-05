using Azure;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WebApi.Configuration.Options;
using WebApi.Services.AI.Tools;
using WebApi.Services.AI.Workflows;

namespace WebApi.Configuration;

public static class AiConfiguration
{
    /// <summary>
    /// Configures AI-related services for Agent Framework using Microsoft.Agents.AI.OpenAI.
    /// Misconfiguration of Azure OpenAI will throw and fail fast.
    /// </summary>
    public static void ConfigureAi(this IServiceCollection services)
    {
        // Validate configuration first
        services.AddOptions<AzureOpenAiSettings>()
            .BindConfiguration(AzureOpenAiSettings.SectionName)
            .Validate(settings =>
            {
                if (string.IsNullOrWhiteSpace(settings.Endpoint) ||
                    string.IsNullOrWhiteSpace(settings.DeploymentName) ||
                    string.IsNullOrWhiteSpace(settings.ApiKey))
                {
                    throw new InvalidOperationException(
                        "Azure OpenAI is not properly configured. " +
                        "Please set 'AzureOpenAI:Endpoint', 'AzureOpenAI:ApiKey', and 'AzureOpenAI:DeploymentName' in configuration.");
                }
                return true;
            })
            .ValidateOnStart();

        // Register IChatClient for Agent Framework (Azure OpenAI)
        services.AddSingleton<IChatClient>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<AzureOpenAiSettings>>().Value;
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("AiConfiguration");

            var endpoint = new Uri(settings.Endpoint);
            var credential = new AzureKeyCredential(settings.ApiKey);
            var client = new AzureOpenAIClient(endpoint, credential);
            var chatClient = client.GetChatClient(settings.DeploymentName).AsIChatClient();

            logger.LogInformation("Azure OpenAI IChatClient configured with deployment: {DeploymentName}",
                settings.DeploymentName);

            return chatClient;
        });

        // Register IEmbeddingGenerator for embeddings (Azure OpenAI)
        // Register as generic IEmbeddingGenerator<string, Embedding<float>> for use with GenerateAsync extension method
        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<AzureOpenAiSettings>>().Value;
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("AiConfiguration");

            var embeddingDeploymentName = settings.EmbeddingDeploymentName ?? settings.DeploymentName;

            // Create Azure OpenAI client and use Microsoft.Agents.AI.OpenAI extension method
            var endpoint = new Uri(settings.Endpoint);
            var credential = new AzureKeyCredential(settings.ApiKey);
            var embeddingGenerator = new AzureOpenAIClient(endpoint, credential)
                .GetEmbeddingClient(embeddingDeploymentName)
                .AsIEmbeddingGenerator();

            logger.LogInformation("Azure OpenAI IEmbeddingGenerator configured with deployment: {EmbeddingDeploymentName}",
                embeddingDeploymentName);

            return embeddingGenerator;
        });

        // Also register as non-generic IEmbeddingGenerator for backward compatibility
        services.AddSingleton<IEmbeddingGenerator>(sp => sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>());

        // Register workflows (scoped - they use scoped services like ConversationContextService)
        services.AddScoped<AssessmentWorkflow>();
        services.AddScoped<SymptomTrackingWorkflow>();

        // Register tools (scoped - they use scoped services like ConversationContextService)
        services.AddScoped<AssessmentTools>();
        services.AddScoped<SymptomTrackerTools>();
    }
}
