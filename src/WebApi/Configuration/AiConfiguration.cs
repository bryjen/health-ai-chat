using Azure;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using WebApi.Configuration.Options;
using WebApi.Services.AI.Tools;
using WebApi.Services.AI.Workflows;

namespace WebApi.Configuration;

public static class AiConfiguration
{
    /// <summary>
    /// Result containing agent builders that need to be mapped as HTTP endpoints.
    /// </summary>
    public class AgentBuildersResult
    {
        public IHostedAgentBuilder HealthChatAgent { get; set; } = null!;
        public IHostedAgentBuilder AssessmentWorkflowAgent { get; set; } = null!;
        public IHostedAgentBuilder SymptomTrackingWorkflowAgent { get; set; } = null!;
    }

    /// <summary>
    /// Configures AI-related services for Agent Framework using Microsoft.Agents.AI.OpenAI.
    /// This overload uses IHostApplicationBuilder to register keyed chat clients for agent discovery.
    /// Misconfiguration of Azure OpenAI will throw and fail fast.
    /// </summary>
    /// <returns>Agent builders that need to be mapped as HTTP endpoints.</returns>
    public static AgentBuildersResult ConfigureAi(this IHostApplicationBuilder builder)
    {
        // Validate configuration first
        builder.Services.AddOptions<AzureOpenAiSettings>()
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

        // Register keyed chat client for Agent Framework (Azure OpenAI)
        // This is required for agent discovery by DevUI
        var azureSettings = builder.Configuration.GetSection(AzureOpenAiSettings.SectionName).Get<AzureOpenAiSettings>()
            ?? throw new InvalidOperationException("Azure OpenAI settings not found in configuration.");

        // Register keyed chat client using AddKeyedSingleton
        builder.Services.AddKeyedSingleton<IChatClient>("chat-model", (sp, key) =>
        {
            var client = new AzureOpenAIClient(
                new Uri(azureSettings.Endpoint),
                new AzureKeyCredential(azureSettings.ApiKey));
            var chatClient = client.GetChatClient(azureSettings.DeploymentName)
                .AsIChatClient();

            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("AiConfiguration");
            logger.LogInformation("Azure OpenAI keyed IChatClient configured with deployment: {DeploymentName}",
                azureSettings.DeploymentName);

            return chatClient;
        });

        // Register singleton IChatClient for backward compatibility (gets from keyed service)
        builder.Services.AddSingleton<IChatClient>(sp =>
        {
            return sp.GetRequiredKeyedService<IChatClient>("chat-model");
        });

        // Register IEmbeddingGenerator for embeddings (Azure OpenAI)
        // Register as generic IEmbeddingGenerator<string, Embedding<float>> for use with GenerateAsync extension method
        builder.Services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
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
        builder.Services.AddSingleton<IEmbeddingGenerator>(sp => sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>());

        // Register workflows (scoped - they use scoped services like ConversationContextService)
        builder.Services.AddScoped<AssessmentWorkflow>();
        builder.Services.AddScoped<SymptomTrackingWorkflow>();

        // Register tools (scoped - they use scoped services like ConversationContextService)
        builder.Services.AddScoped<AssessmentTools>();
        builder.Services.AddScoped<SymptomTrackerTools>();

        // Register agents for DevUI discovery
        var healthChatAgentBuilder = builder.AddAIAgent(
            "health-chat",
            instructions: "You are a healthcare assistant that helps users track symptoms and create health assessments. " +
                         "You can help users report symptoms, track symptom episodes, and generate health assessments based on their symptoms.",
            description: "Health chat agent for symptom tracking and assessments",
            chatClientServiceKey: "chat-model")
            .WithInMemorySessionStore();

        // Register workflows as agents for DevUI discovery
        // Note: These are wrapper registrations - the actual workflow execution still uses the scoped services above
        var assessmentWorkflowAgent = builder.AddAIAgent(
            "assessment-workflow",
            instructions: "You are an assessment workflow agent that helps create health assessments based on user symptoms.",
            description: "Workflow for creating health assessments from user symptoms",
            chatClientServiceKey: "chat-model")
            .WithInMemorySessionStore();

        var symptomTrackingWorkflowAgent = builder.AddAIAgent(
            "symptom-tracking-workflow",
            instructions: "You are a symptom tracking workflow agent that helps users track and manage their symptoms.",
            description: "Workflow for tracking and managing user symptoms",
            chatClientServiceKey: "chat-model")
            .WithInMemorySessionStore();

        // Return agent builders so they can be mapped as HTTP endpoints
        return new AgentBuildersResult
        {
            HealthChatAgent = healthChatAgentBuilder,
            AssessmentWorkflowAgent = assessmentWorkflowAgent,
            SymptomTrackingWorkflowAgent = symptomTrackingWorkflowAgent
        };
    }
}
