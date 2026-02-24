using Anthropic;
using Anthropic.Core;
using Anthropic.Models.Messages;
using Azure;
using Azure.AI.OpenAI;
using FluentValidation;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using WebApi.Configuration.Options;
using WebApi.Services;
using WebApi.Services.Chat;
using WebApi.Services.Chat.Formatters;
using WebApi.Services.Chat.HistoryProviders;
using WebApi.Services.Chat.Plugins;
using WebApi.Services.Chat.Response;
using WebApi.Services.Data;
using static WebApi.Services.Chat.Middleware.AgentMiddleware;

using MessageCreateParams = Anthropic.Models.Messages.MessageCreateParams;
using Converter = WebApi.Configuration.AgentProviderConverter;

// ReSharper disable UnusedMethodReturnValue.Global
#pragma warning disable CS8524 // The switch expression does not handle some values of its input type (it is not exhaustive) involving an unnamed enum value.
#pragma warning disable OPENAI001
#pragma warning disable MEAI001

namespace WebApi.Configuration;

public static class AiConfiguration
{
    /// <summary>
    /// Registers AI services, agents, and chat orchestration components.
    /// </summary>
    /// <remarks>
    /// Registers the following services (as of 2026/02/06):
    /// <list type="bullet">
    /// <item><description>AI agent plugins (AssessmentPlugin, SymptomPlugin)</description></item>
    /// <item><description>Keyed AI agents for Microsoft Foundry and Anthropic providers</description></item>
    /// <item><description>Core chat services (MessageFormatter, SessionManager, ChatService)</description></item>
    /// <item><description>Console-specific services (ConsoleChatOrchestrator, ConsoleResponseWriter)</description></item>
    /// </list>
    /// </remarks>
    public static IServiceCollection ConfigureCoreAiAgents(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var aiOptions = configuration.GetSection(AiOptions.SectionName).Get<AiOptions>()
                        ?? throw new InvalidOperationException("Failed to bind AiOptions from configuration.");

        services.AddScoped<AssessmentPlugin>();
        services.AddScoped<SymptomTrackerPlugin>();

        // agents
        // scoped registration to allow for scoped-data. overhead of agent creation deemed to be a non-issue here.
        services.AddKeyedScoped<AIAgent>(Converter.ToStringValue(AgentProvider.Anthropic), (sp, _)
            => CreateMainAgentCallback(sp, aiOptions, AgentProvider.Anthropic));
        services.AddKeyedScoped<AIAgent>(Converter.ToStringValue(AgentProvider.MicrosoftFoundry), (sp, _)
            => CreateMainAgentCallback(sp, aiOptions, AgentProvider.MicrosoftFoundry));
        services.AddScoped<AIAgent>(sp => sp.GetKeyedService<AIAgent>(Converter.ToStringValue(AgentProvider.Anthropic))
                                          ?? throw new InvalidOperationException());

        // core services, no ui dependencies
        services.AddSingleton<MessageFormatter>();
        services.AddScoped<SessionManager>();
        services.AddScoped<ChatService>();

        services.AddScoped<AiState>();
        services.AddScoped<AppointmentService>();
        services.AddScoped<AssessmentService>();
        services.AddScoped<EpisodeService>();
        services.AddScoped<SymptomService>();

        // console-specific orchestrators
        services.AddScoped<Services.Console.ConsoleChatOrchestrator>();
        services.AddScoped<ResponseWriter, ConsoleResponseWriter>();
        services.AddScoped<HttpResponseWriter>();

        return services;
    }


    private static AIAgent CreateMainAgentCallback(IServiceProvider sp, AiOptions aiOptions, AgentProvider provider)
    {
        var serviceScopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var defaultModelName = provider switch
        {
            AgentProvider.MicrosoftFoundry => aiOptions.MicrosoftFoundry.ModelDeploymentName,
            AgentProvider.Anthropic => aiOptions.Anthropic.DefaultModel,
        };
        var chatClientBuilder = provider switch
        {
            AgentProvider.MicrosoftFoundry => ConstructFoundryBaseChatClient(aiOptions,sp.GetRequiredService<IValidator<MicrosoftFoundryOptions>>()),
            AgentProvider.Anthropic => ConstructAnthropicBaseChatClient(aiOptions,sp.GetRequiredService<IValidator<AnthropicOptions>>()),
        };
        var baseChatClient = chatClientBuilder
            .Use(getResponseFunc: ChatClientMiddleware, getStreamingResponseFunc: ChatClientStreamingMiddleware)
            .Build();

        var assessmentPlugin = sp.GetRequiredService<AssessmentPlugin>();
        var symptomTrackerPlugin = sp.GetRequiredService<SymptomTrackerPlugin>();
        IList<AITool> tools = [
            .. assessmentPlugin.AsAiTools(),
            .. symptomTrackerPlugin.AsAiTools()
        ];

        // "core" agent creation
        return baseChatClient
            .AsAIAgent(new ChatClientAgentOptions
            {
                Name = Converter.ToStringValue(provider),
                Description = $"Primary agent for handling user interactions, powered by `{provider}`.",
                ChatOptions = new ChatOptions
                {
                    ModelId = defaultModelName,
                    Instructions = "You are a helpful assistant.",
                    Tools = tools
                },
                ChatHistoryProviderFactory = (ctx, _) => new ValueTask<ChatHistoryProvider>(
                    new DbChatHistoryProvider(serviceScopeFactory, ctx.SerializedState))
            })
            .AsBuilder()
            .Use(FunctionCallMiddleware)
            .Build();
    }

    /*
     * REMARK: different providers differ in the initial client creation. From there, they can be "rejoined" to the same
     * pipeline by having them as builders.
     */

#region Helpers
    /// Initializes a <see cref="ChatClientBuilder"/> for Microsoft Foundry.
    private static ChatClientBuilder ConstructFoundryBaseChatClient(
        AiOptions aiOptions,
        IValidator<MicrosoftFoundryOptions> validator)
    {
        var validationResult = validator.Validate(aiOptions.MicrosoftFoundry);
        if (!validationResult.IsValid)
        {
            var errorMessages = string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage));
            throw new InvalidOperationException(
                $"Microsoft Foundry configuration validation failed: {errorMessages}");
        }

        var endpoint = new Uri(aiOptions.MicrosoftFoundry.Endpoint);
        var credential = new AzureKeyCredential(aiOptions.MicrosoftFoundry.Key);
        return new AzureOpenAIClient(endpoint, credential)
            .GetChatClient(aiOptions.MicrosoftFoundry.ModelDeploymentName)
            .AsIChatClient()
            .AsBuilder();
    }

    /// Initializes a <see cref="ChatClientBuilder"/> for the Anthropic API.
    private static ChatClientBuilder ConstructAnthropicBaseChatClient(
        AiOptions aiOptions,
        IValidator<AnthropicOptions> validator)
    {
        var validationResult = validator.Validate(aiOptions.Anthropic);
        if (!validationResult.IsValid)
        {
            var errorMessages = string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage));
            throw new InvalidOperationException(
                $"Anthropic configuration validation failed: {errorMessages}");
        }

        return new AnthropicClient(new ClientOptions { APIKey = aiOptions.Anthropic.ApiKey })
            .AsIChatClient()
            .AsBuilder()
            .ConfigureOptions(o => o.RawRepresentationFactory = chat => RawRepresentationCallback(o, chat));
    }

    /// Configures the message creation options for Anthropic API calls.
    private static MessageCreateParams RawRepresentationCallback(ChatOptions options, IChatClient chat)
    {
        return new MessageCreateParams
        {
            Model = options.ModelId ?? "claude-haiku-4-5",
            MaxTokens = options.MaxOutputTokens ?? 4096,
            Messages = [],
            Thinking = new ThinkingConfigParam(new ThinkingConfigEnabled(budgetTokens: 1024))
        };
    }
#endregion
}

public static class AgentProviderConverter
{
    public static AgentProvider ToEnum(string value) => value switch
    {
        "microsoft-foundry" => AgentProvider.MicrosoftFoundry,
        "anthropic" => AgentProvider.Anthropic,
        _ => throw new ArgumentException($"Unknown provider: {value}")
    };

    public static string ToStringValue(AgentProvider provider) => provider switch
    {
        AgentProvider.MicrosoftFoundry => "microsoft-foundry",
        AgentProvider.Anthropic => "anthropic",
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };
}
