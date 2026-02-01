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
public partial class HealthChatScenario : AiScenarioHandler<HealthChatScenarioRequest, HealthChatScenarioResponse>
{
    private const string SystemPromptTemplate = @"You are a helpful healthcare assistant. Your role is to:
1. Listen to users' health concerns and symptoms
2. Track symptoms using the available functions - ALWAYS call CreateSymptomWithEpisode when a user reports a symptom
3. Progressively explore symptoms to understand them better
4. Assess situations and provide recommendations
5. Provide helpful health guidance

## Multi-Turn Behavior

You can make multiple turns to gather information before responding:
- Call functions to track symptoms, get episode details, or create assessments
- Analyze the function results
- Call additional functions if needed
- When you have all the information you need, provide your final response without calling any functions
- You decide when you're done - provide your final answer when you're ready

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
        return await ExecuteAsyncInternal(input, cancellationToken, (IStatusUpdateService?)null);
    }

    public async Task<HealthChatScenarioResponse> ExecuteAsyncInternal(
        HealthChatScenarioRequest input,
        CancellationToken cancellationToken,
        IStatusUpdateService? statusUpdateService)
    {
        return await ExecuteAsyncImpl(input, cancellationToken, statusUpdateService);
    }

    private async Task<HealthChatScenarioResponse> ExecuteAsyncImpl(
        HealthChatScenarioRequest input,
        CancellationToken cancellationToken,
        IStatusUpdateService? statusUpdateService)
    {
        // Track status updates sent during processing
        var statusUpdatesSent = new List<object>();
        
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
            var responseText = await GetChatCompletionWithKernelAsync(
                requestKernel, 
                chatHistory, 
                input.ConnectionId,
                statusUpdateService,
                cancellationToken,
                statusUpdatesSent);

            // Flush context changes
            await _contextService.FlushContextAsync(conversationContext);

            var response = CreateResponse(responseText);
            response.StatusUpdatesSent = statusUpdatesSent;
            
            return response;
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
        string? connectionId = null,
        IStatusUpdateService? statusUpdateService = null,
        CancellationToken cancellationToken = default,
        List<object>? statusUpdatesSent = null)
    {
        const int maxTurns = 15;
        var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
        var executionSettings = new OpenAIPromptExecutionSettings
        {
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
        };

        int turnCount = 0;
        string? finalResponse = null;
        var initialHistoryCount = chatHistory.Count;

        while (turnCount < maxTurns)
        {
            turnCount++;
            Logger.LogDebug("Multi-turn agentic loop: Turn {TurnNumber}/{MaxTurns}", turnCount, maxTurns);

            try
            {
                var response = await chatCompletionService.GetChatMessageContentsAsync(
                    chatHistory,
                    executionSettings,
                    kernel,
                    cancellationToken: cancellationToken);

                var assistantMessage = response.FirstOrDefault();
                
                if (assistantMessage == null)
                {
                    Logger.LogWarning("No assistant message returned on turn {TurnNumber}", turnCount);
                    break;
                }

                // Check if response has content (final response)
                // With AutoInvokeKernelFunctions, function calls are handled internally and the model
                // is called again until it provides a final response. However, we want explicit control,
                // so we check if the chat history grew (indicating function calls were made).
                bool hasContent = !string.IsNullOrWhiteSpace(assistantMessage.Content);
                
                // Check if chat history grew (indicates function calls were made and results added)
                var historyGrew = chatHistory.Count > initialHistoryCount + turnCount;
                
                if (historyGrew)
                {
                    Logger.LogDebug("Turn {TurnNumber}: Chat history grew (from {Initial} to {Current}), indicating function calls were made", 
                        turnCount, initialHistoryCount + turnCount, chatHistory.Count);
                }

                // If we have content, this is likely the final response
                // However, with AutoInvokeKernelFunctions, the model might still want to make more calls
                // So we check if the history grew - if it did, functions were called and we should continue
                if (hasContent && !historyGrew)
                {
                    // Final response - no function calls were made, we have content
                    // Send a brief delay to ensure status messages are displayed before final message
                    if (connectionId != null && statusUpdateService != null && statusUpdatesSent != null && statusUpdatesSent.Any())
                    {
                        await Task.Delay(1000); // Longer delay to ensure all status messages appear first
                    }
                    
                    finalResponse = assistantMessage.Content ?? string.Empty;
                    Logger.LogDebug("Turn {TurnNumber}: Model provided final response (length: {Length})", 
                        turnCount, finalResponse.Length);
                    break;
                }

                // If history grew, function calls were made and we should continue
                // Add the assistant message to history and continue
                if (historyGrew)
                {
                    Logger.LogDebug("Turn {TurnNumber}: Functions were invoked (history grew), continuing to next turn", turnCount);
                    
                    // Detect assessment creation by checking recent messages
                    if (connectionId != null && statusUpdateService != null)
                    {
                        var (assessmentCreated, assessmentId, hypothesis, confidence) = DetectAssessmentCreationWithDetails(chatHistory);
                        if (assessmentCreated)
                        {
                            // Send status updates in correct order with delays
                            // Order: Complete → Created → Analyzing (before final response)
                            // 1. Assessment complete (after creation)
                            await statusUpdateService.SendAssessmentCompleteAsync(connectionId);
                            statusUpdatesSent?.Add(new
                            {
                                type = "assessment-complete",
                                message = "Assessment complete",
                                timestamp = DateTime.UtcNow
                            });
                            await Task.Delay(800); // Delay for UI flow
                            
                            // 2. Send assessment-created status with details if we have them
                            if (assessmentId > 0 && !string.IsNullOrWhiteSpace(hypothesis))
                            {
                                await statusUpdateService.SendAssessmentCreatedAsync(connectionId, assessmentId, hypothesis, confidence);
                                statusUpdatesSent?.Add(new
                                {
                                    type = "assessment-created",
                                    assessmentId = assessmentId,
                                    hypothesis = hypothesis,
                                    confidence = confidence,
                                    timestamp = DateTime.UtcNow
                                });
                                await Task.Delay(800); // Delay for UI flow
                            }
                            
                            // 3. Analyzing assessment (BEFORE final response - analyzing the completed assessment)
                            await statusUpdateService.SendAnalyzingAssessmentAsync(connectionId);
                            statusUpdatesSent?.Add(new
                            {
                                type = "assessment-analyzing",
                                message = "Analyzing assessment...",
                                timestamp = DateTime.UtcNow
                            });
                            await Task.Delay(800); // Delay before final response
                        }
                        else if (turnCount == 1)
                        {
                            // On first turn, check if model might be generating assessment
                            // This is a heuristic - we check if the user message suggests assessment generation
                            var mightGenerateAssessment = chatHistory.Any(m => 
                                m.Role == AuthorRole.User && 
                                (m.Content?.Contains("assessment", StringComparison.OrdinalIgnoreCase) == true ||
                                 m.Content?.Contains("diagnosis", StringComparison.OrdinalIgnoreCase) == true ||
                                 m.Content?.Contains("what do you think", StringComparison.OrdinalIgnoreCase) == true));
                            
                            if (mightGenerateAssessment)
                            {
                                await statusUpdateService.SendGeneratingAssessmentAsync(connectionId);
                                statusUpdatesSent?.Add(new
                                {
                                    type = "assessment-generating",
                                    message = "Generating assessment...",
                                    timestamp = DateTime.UtcNow
                                });
                                await Task.Delay(800); // Delay for UI flow
                            }
                        }
                    }
                    
                    // Add assistant message to history if it has content
                    // Function results are already added by AutoInvokeKernelFunctions
                    if (hasContent && !string.IsNullOrWhiteSpace(assistantMessage.Content))
                    {
                        chatHistory.AddAssistantMessage(assistantMessage.Content);
                    }
                    
                    // Update initial count for next iteration
                    initialHistoryCount = chatHistory.Count;
                    continue;
                }

                // If we have content but history didn't grow, this should be final
                if (hasContent)
                {
                    finalResponse = assistantMessage.Content;
                    Logger.LogDebug("Turn {TurnNumber}: Model provided final response (length: {Length})", 
                        turnCount, finalResponse.Length);
                    break;
                }

                // If we have neither content nor history growth, something unexpected happened
                Logger.LogWarning("Turn {TurnNumber}: Unexpected response - no content and no history growth", turnCount);
                finalResponse = assistantMessage.Content ?? "I apologize, but I couldn't generate a response.";
                break;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error on turn {TurnNumber} of multi-turn loop", turnCount);
                throw;
            }
        }

        if (turnCount >= maxTurns)
        {
            Logger.LogWarning("Multi-turn loop reached maximum turns ({MaxTurns}). Using last response.", maxTurns);
            // Get the last assistant message from history as fallback
            var lastAssistantMessage = chatHistory
                .Where(m => m.Role == AuthorRole.Assistant)
                .LastOrDefault();
            
            if (lastAssistantMessage != null && !string.IsNullOrWhiteSpace(lastAssistantMessage.Content))
            {
                finalResponse = lastAssistantMessage.Content;
            }
        }

        if (string.IsNullOrWhiteSpace(finalResponse))
        {
            Logger.LogWarning("Multi-turn loop completed but no final response generated");
            finalResponse = "I apologize, but I couldn't generate a response.";
        }

        Logger.LogDebug("Multi-turn loop completed in {TurnCount} turns. Final response length: {Length}", 
            turnCount, finalResponse.Length);
        
        return finalResponse;
    }

    private (bool Created, int AssessmentId, string Hypothesis, decimal Confidence) DetectAssessmentCreationWithDetails(ChatHistory chatHistory)
    {
        // Check recent messages for assessment creation indicators
        // Look for function results that mention "Created assessment" or assessment IDs
        var recentMessages = chatHistory
            .TakeLast(5)
            .ToList();

        foreach (var message in recentMessages)
        {
            // Check if message content contains assessment creation
            if (!string.IsNullOrWhiteSpace(message.Content))
            {
                // Try to parse assessment details from the message
                // Format: "Created assessment {id}: {hypothesis} (confidence: {confidence:P0})"
                var content = message.Content;
                if (content.Contains("Created assessment", StringComparison.OrdinalIgnoreCase))
                {
                    // Try to extract assessment ID, hypothesis, and confidence
                    var idMatch = System.Text.RegularExpressions.Regex.Match(
                        content, 
                        @"Created assessment (\d+):\s*([^(]+)\s*\(confidence:\s*(\d+(?:\.\d+)?)%?\)",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    
                    if (idMatch.Success && idMatch.Groups.Count >= 4)
                    {
                        if (int.TryParse(idMatch.Groups[1].Value, out var assessmentId))
                        {
                            var hypothesis = idMatch.Groups[2].Value.Trim();
                            if (decimal.TryParse(idMatch.Groups[3].Value, out var confidencePercent))
                            {
                                // Convert percentage to decimal (e.g., 85% -> 0.85)
                                var confidence = confidencePercent / 100m;
                                return (true, assessmentId, hypothesis, confidence);
                            }
                        }
                    }
                    
                    // Fallback: just detect creation
                    return (true, 0, string.Empty, 0m);
                }
            }

            // Check message items for function call results
            if (message.Items != null)
            {
                foreach (var item in message.Items)
                {
                    var itemText = item.ToString();
                    if (!string.IsNullOrWhiteSpace(itemText) &&
                        itemText.Contains("Created assessment", StringComparison.OrdinalIgnoreCase))
                    {
                        // Try to parse from item text
                        var idMatch = System.Text.RegularExpressions.Regex.Match(
                            itemText, 
                            @"Created assessment (\d+):\s*([^(]+)\s*\(confidence:\s*(\d+(?:\.\d+)?)%?\)",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        
                        if (idMatch.Success && idMatch.Groups.Count >= 4)
                        {
                            if (int.TryParse(idMatch.Groups[1].Value, out var assessmentId))
                            {
                                var hypothesis = idMatch.Groups[2].Value.Trim();
                                if (decimal.TryParse(idMatch.Groups[3].Value, out var confidencePercent))
                                {
                                    var confidence = confidencePercent / 100m;
                                    return (true, assessmentId, hypothesis, confidence);
                                }
                            }
                        }
                        
                        return (true, 0, string.Empty, 0m);
                    }
                }
            }
        }

        return (false, 0, string.Empty, 0m);
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
            Message = responseText,
            StatusUpdatesSent = new List<object>()
        };
    }
}
