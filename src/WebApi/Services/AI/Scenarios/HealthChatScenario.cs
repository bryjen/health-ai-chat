using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Embeddings;
using Web.Common.DTOs.AI;
using WebApi.Configuration.Options;
using WebApi.Models;
using WebApi.Services.AI.Plugins;
using WebApi.Services.Chat;
using WebApi.Services.VectorStore;

namespace WebApi.Services.AI.Scenarios;

/// <summary>
/// Concrete implementation of the health chat AI scenario.
/// Handles symptom tracking and appointment booking conversations.
/// </summary>
public class HealthChatScenario : AiScenarioHandler<HealthChatScenarioRequest, HealthChatScenarioResponse>
{
    private const string SystemPromptTemplate = @"You are a helpful healthcare assistant. Your role is to:
1. Listen to users' health concerns and symptoms
2. Track symptoms using the available functions - ALWAYS call CreateSymptomWithEpisode when a user reports a symptom
3. Progressively explore symptoms to understand them better
4. Assess situations and provide recommendations
5. Provide helpful health guidance

## Symptom Tracking

When a user mentions a symptom:
1. Check if they have a recent episode of this symptom using GetActiveEpisodes()
2. If yes: ask ""Is this related to the [symptom] from [date], or something new?""
   - If same: work with existing episode using UpdateEpisode
   - If new: CreateSymptomWithEpisode()
3. If no recent episode: CreateSymptomWithEpisode()

After creating/identifying an episode, explore it by asking about (one at a time):
- Severity (1-10 scale)
- Location (where exactly?)
- When it started (already captured)
- Frequency (constant, comes and goes?)
- Triggers (what makes it worse?)
- Relievers (what helps?)
- Pattern (time of day, after activities?)

Call UpdateEpisode() as you learn each detail. Don't ask about fields already filled.

When user denies a symptom (""no fever"", ""I don't have nausea""), call RecordNegativeFinding().

## Assessment

When you have enough information (episode stage = ""characterized"" for primary symptoms):
1. Formulate a hypothesis based on:
   - Active episodes and their details
   - Negative findings (what's ruled out)
   - Symptom patterns and combinations
2. Call CreateAssessment() with your reasoning
3. Share the assessment with the user in plain language

## Phase Awareness

Track conversation phase:
- GATHERING: Still collecting symptoms, asking follow-ups
- ASSESSING: Enough info, forming/sharing assessment
- RECOMMENDING: Assessment complete, discussing next steps

IMPORTANT: You must respond with valid JSON in this exact format:
{
  ""message"": ""Your response message to the user"",
  ""appointment"": {
    ""needed"": true/false,
    ""urgency"": ""Emergency"" | ""High"" | ""Medium"" | ""Low"" | ""None"",
    ""symptoms"": [""symptom1"", ""symptom2""],
    ""duration"": 30,
    ""readyToBook"": true/false,
    ""followUpNeeded"": true/false,
    ""nextQuestions"": [""question1"", ""question2""],
    ""preferredTime"": null,
    ""emergencyAction"": ""Call 911 immediately"" (only for emergencies)
  },
  ""symptomChanges"": [
    {""symptom"": ""headache"", ""action"": ""added""}
  ]
}

Always call the appropriate functions when users report symptoms or request actions. Always return valid JSON.";

    private readonly VectorStoreService _vectorStoreService;
    private readonly VectorStoreSettings _vectorStoreSettings;
    private readonly ConversationContextService _contextService;
    private readonly IServiceProvider _serviceProvider;

    public HealthChatScenario(
        [FromKeyedServices("health")] Kernel kernel,
        VectorStoreService vectorStoreService,
        IOptions<VectorStoreSettings> vectorStoreSettings,
        ConversationContextService contextService,
        IServiceProvider serviceProvider,
        ILogger<HealthChatScenario> logger)
        : base(kernel, logger)
    {
        _vectorStoreService = vectorStoreService;
        _vectorStoreSettings = vectorStoreSettings.Value;
        _contextService = contextService;
        _serviceProvider = serviceProvider;
    }

    protected override string GetSystemPrompt()
    {
        return SystemPromptTemplate;
    }

    protected override async Task<string?> GetEmbeddingsContextAsync(
        HealthChatScenarioRequest input,
        CancellationToken cancellationToken = default)
    {
        // Not used - ExecuteAsync is overridden and handles context enrichment directly
        return null;
    }

    protected override ChatHistory BuildChatHistory(HealthChatScenarioRequest input, string? context)
    {
        // Not used - ExecuteAsync is overridden and builds chat history directly
        // Required by abstract base class contract
        return new ChatHistory();
    }

    public override async Task<HealthChatScenarioResponse> ExecuteAsync(
        HealthChatScenarioRequest input,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Hydrate conversation context
            var conversationContext = await _contextService.HydrateContextAsync(
                input.UserId,
                input.ConversationId);

            // Create plugins and set their context
            var symptomTrackerPlugin = _serviceProvider.GetRequiredService<SymptomTrackerPlugin>();
            symptomTrackerPlugin.SetContext(conversationContext, input.UserId);

            var assessmentPlugin = _serviceProvider.GetRequiredService<AssessmentPlugin>();
            if (input.ConversationId.HasValue)
            {
                assessmentPlugin.SetContext(conversationContext, input.UserId, input.ConversationId.Value);
            }

            // Create a kernel instance for this request with plugins
            var chatCompletionService = Kernel.GetRequiredService<IChatCompletionService>();
            var kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.Services.AddSingleton(chatCompletionService);
            var embeddingService = Kernel.Services.GetService<ITextEmbeddingGenerationService>();
            if (embeddingService != null)
            {
                kernelBuilder.Services.AddSingleton(embeddingService);
            }
            var requestKernel = kernelBuilder.Build();
            
            // Add plugins to kernel
            requestKernel.Plugins.AddFromObject(symptomTrackerPlugin, "SymptomTracker");
            requestKernel.Plugins.AddFromObject(assessmentPlugin, "Assessment");

            // Get conversation context messages
            var contextMessages = await GetConversationContextAsync(input.ConversationId, cancellationToken);
            
            // Enrich with cross-conversation messages
            contextMessages = await EnrichContextWithCrossConversationMessagesAsync(
                input.UserId,
                input.Message,
                input.ConversationId,
                contextMessages,
                cancellationToken);
            
            // Limit context messages
            contextMessages = LimitContextMessages(contextMessages);

            // Build system prompt with context summary
            var systemPrompt = BuildSystemPromptWithContext(conversationContext);

            // Build chat history
            var chatHistory = new ChatHistory();
            chatHistory.AddSystemMessage(systemPrompt);

            // Add context messages in chronological order
            foreach (var msg in contextMessages.OrderBy(m => m.CreatedAt))
            {
                var role = msg.Role.ToLowerInvariant() switch
                {
                    "user" => AuthorRole.User,
                    "assistant" => AuthorRole.Assistant,
                    _ => AuthorRole.User
                };
                chatHistory.AddMessage(role, msg.Content);
            }

            // Add current user message
            chatHistory.AddUserMessage(input.Message);

            // Get AI response using request kernel with plugins
            var responseText = await GetChatCompletionWithKernelAsync(requestKernel, chatHistory, cancellationToken);

            // Flush context changes
            await _contextService.FlushContextAsync(conversationContext);

            return CreateResponse(responseText);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error executing health chat scenario for user {UserId}", input.UserId);
            throw;
        }
    }

    private string BuildSystemPromptWithContext(ConversationContext context)
    {
        var prompt = SystemPromptTemplate;

        if (context.ActiveEpisodes.Any())
        {
            var episodesSummary = string.Join(", ", 
                context.ActiveEpisodes.Select(e => $"{e.Symptom?.Name ?? "Unknown"} (stage: {e.Stage})"));
            prompt += $"\n\nCurrent active episodes: {episodesSummary}";
        }

        if (context.NegativeFindings.Any())
        {
            var negativeSummary = string.Join(", ", 
                context.NegativeFindings.Select(nf => nf.SymptomName));
            prompt += $"\n\nRecent negative findings (user confirmed they don't have): {negativeSummary}";
        }

        if (context.CurrentAssessment != null)
        {
            prompt += $"\n\nCurrent assessment: {context.CurrentAssessment.Hypothesis} (confidence: {context.CurrentAssessment.Confidence:P0})";
        }

        return prompt;
    }

    private async Task<string> GetChatCompletionWithKernelAsync(
        Kernel kernel,
        ChatHistory chatHistory,
        CancellationToken cancellationToken = default)
    {
        var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
        var executionSettings = new Microsoft.SemanticKernel.Connectors.OpenAI.OpenAIPromptExecutionSettings
        {
            ToolCallBehavior = Microsoft.SemanticKernel.Connectors.OpenAI.ToolCallBehavior.AutoInvokeKernelFunctions
        };

        var response = await chatCompletionService.GetChatMessageContentsAsync(
            chatHistory,
            executionSettings,
            kernel,
            cancellationToken: cancellationToken);

        var assistantMessage = response.FirstOrDefault();
        var responseText = assistantMessage?.Content ?? "I apologize, but I couldn't generate a response.";

        Logger.LogDebug("Generated AI response of length {Length} characters", responseText.Length);
        return responseText;
    }

    private async Task<List<Message>> GetConversationContextAsync(
        Guid? conversationId,
        CancellationToken cancellationToken)
    {
        if (conversationId.HasValue)
        {
            var messages = await _vectorStoreService.GetConversationMessagesAsync(conversationId.Value);
            Logger.LogDebug("Retrieved {Count} messages from conversation {ConversationId}", 
                messages.Count, conversationId.Value);
            return messages;
        }

        Logger.LogDebug("No conversation ID provided, using empty context");
        return new List<Message>();
    }

    private async Task<List<Message>> EnrichContextWithCrossConversationMessagesAsync(
        Guid userId,
        string userMessage,
        Guid? conversationId,
        List<Message> contextMessages,
        CancellationToken cancellationToken)
    {
        if (!_vectorStoreSettings.EnableCrossConversationSearch || 
            contextMessages.Count >= _vectorStoreSettings.CrossConversationSearchThreshold)
        {
            return contextMessages;
        }

        try
        {
            var relevantPastMessages = await _vectorStoreService.SearchSimilarMessagesAsync(
                userId,
                userMessage,
                excludeConversationId: conversationId,
                limit: _vectorStoreSettings.MaxCrossConversationResults,
                minSimilarity: _vectorStoreSettings.MinSimilarityScore);

            if (relevantPastMessages.Any())
            {
                Logger.LogDebug(
                    "Found {Count} relevant messages from other conversations (current conversation has {CurrentCount} messages)",
                    relevantPastMessages.Count,
                    contextMessages.Count);

                // Combine: past messages (semantic) + current conversation (chronological)
                // Past messages are ordered by creation date to maintain some chronological sense
                return relevantPastMessages
                    .OrderBy(m => m.CreatedAt)
                    .Concat(contextMessages)
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            // Log but don't fail - fall back to just current conversation context
            Logger.LogWarning(ex, 
                "Error performing cross-conversation semantic search, using only current conversation context");
        }

        return contextMessages;
    }

    private List<Message> LimitContextMessages(List<Message> contextMessages)
    {
        if (_vectorStoreSettings.MaxContextMessages > 0 && 
            contextMessages.Count > _vectorStoreSettings.MaxContextMessages)
        {
            // Keep the most recent messages
            var limited = contextMessages
                .OrderBy(m => m.CreatedAt)
                .TakeLast(_vectorStoreSettings.MaxContextMessages)
                .ToList();
            Logger.LogDebug("Limited context to {Count} most recent messages", 
                _vectorStoreSettings.MaxContextMessages);
            return limited;
        }

        return contextMessages;
    }

    protected override HealthChatScenarioResponse CreateResponse(string responseText)
    {
        return new HealthChatScenarioResponse
        {
            Message = responseText
        };
    }
}
