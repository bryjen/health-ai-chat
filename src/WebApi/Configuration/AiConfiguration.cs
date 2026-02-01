using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Embeddings;
using WebApi.Configuration.Options;

namespace WebApi.Configuration;

public static class AiConfiguration
{
    /// <summary>
    /// Configures AI-related services, including singleton AI services and keyed kernels.
    /// Misconfiguration of Azure OpenAI will throw and fail fast.
    /// </summary>
    public static void ConfigureAi(this IServiceCollection services)
    {
        // Register singleton AI services (extracted from kernel creation)
        services.AddSingleton<IChatCompletionService>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<AzureOpenAiSettings>>().Value;
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("AiConfiguration");

            if (string.IsNullOrWhiteSpace(settings.Endpoint) ||
                string.IsNullOrWhiteSpace(settings.DeploymentName) ||
                string.IsNullOrWhiteSpace(settings.ApiKey))
            {
                throw new InvalidOperationException(
                    "Azure OpenAI is not properly configured. " +
                    "Please set 'AzureOpenAI:Endpoint', 'AzureOpenAI:ApiKey', and 'AzureOpenAI:DeploymentName' in configuration.");
            }

            var builder = Kernel.CreateBuilder();
            builder.AddAzureOpenAIChatCompletion(
                deploymentName: settings.DeploymentName,
                endpoint: settings.Endpoint,
                apiKey: settings.ApiKey);

            var kernel = builder.Build();
            var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

            logger.LogInformation("Azure OpenAI ChatCompletionService configured with deployment: {DeploymentName}",
                settings.DeploymentName);

            return chatCompletionService;
        });

        // Register embedding service as singleton (if available)
        services.AddSingleton<ITextEmbeddingGenerationService>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<AzureOpenAiSettings>>().Value;
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("AiConfiguration");

            var embeddingDeploymentName = settings.EmbeddingDeploymentName ?? settings.DeploymentName;
            var builder = Kernel.CreateBuilder();
            builder.AddAzureOpenAITextEmbeddingGeneration(
                deploymentName: embeddingDeploymentName,
                endpoint: settings.Endpoint,
                apiKey: settings.ApiKey);

            var kernel = builder.Build();
            var embeddingService = kernel.GetRequiredService<ITextEmbeddingGenerationService>();

            logger.LogInformation("Azure OpenAI TextEmbeddingGenerationService configured with deployment: {EmbeddingDeploymentName}",
                embeddingDeploymentName);

            return embeddingService;
        });

        // Register singleton kernel for services that need a Kernel instance (e.g., DebugController, VectorStoreService)
        services.AddSingleton<Kernel>(sp =>
        {
            var chatCompletionService = sp.GetRequiredService<IChatCompletionService>();
            var embeddingService = sp.GetService<ITextEmbeddingGenerationService>();

            var kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.Services.AddSingleton(chatCompletionService);

            if (embeddingService != null)
            {
                kernelBuilder.Services.AddSingleton(embeddingService);
            }

            return kernelBuilder.Build();
        });

        // Register keyed kernel for HealthChatScenario (reuses the same singleton Kernel instance)
        // Note: Plugins are registered per-request in HealthChatScenario since they need conversation context
        services.AddKeyedSingleton<Kernel>("health", (sp, _) => sp.GetRequiredService<Kernel>());
    }
}
