using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Embeddings;
using Web.Common.DTOs.AI;
using Web.Common.DTOs.Health;
using WebApi.Configuration.Options;
using WebApi.Hubs;
using WebApi.Models;
using WebApi.Services.AI.Plugins;
using WebApi.Services.Chat;
using WebApi.Services.Chat.Conversations;
using WebApi.Services.VectorStore;

namespace WebApi.Services.AI.Scenarios;

/// <summary>
/// Concrete implementation of the health chat AI scenario.
/// Handles symptom tracking and appointment booking conversations.
/// </summary>
public partial class HealthChatScenario(
    [FromKeyedServices("health")] Kernel kernel,
    VectorStoreService vectorStoreService,
    IOptions<VectorStoreSettings> vectorStoreSettings,
    ConversationContextService contextService,
    IServiceProvider serviceProvider,
    ILogger<HealthChatScenario> logger)
    : AiScenarioHandler<HealthChatScenarioRequest, HealthChatScenarioResponse>(kernel, logger)
{
    private const string SystemPromptTemplate = @"You are a helpful healthcare assistant. CRITICAL: You MUST use the available functions to accomplish tasks. DO NOT just describe actions in text - you MUST call the actual functions.

## MANDATORY FUNCTION CALLING RULES

**YOU MUST CALL FUNCTIONS - DO NOT JUST DESCRIBE ACTIONS IN TEXT**

1. **Symptoms**: When a user mentions ANY symptom, you MUST IMMEDIATELY call CreateSymptomWithEpisode(). Example: user says ""I have a headache"" → call CreateSymptomWithEpisode(name=""headache"")
2. **Assessments**: When user asks for assessment OR you have enough info, you MUST call CreateAssessment(). Example: user says ""create assessment"" → call CreateAssessment(hypothesis=""your diagnosis"", confidence=0.7, recommendedAction=""see-gp""). DO NOT describe assessments - CALL THE FUNCTION.
3. **Episode Updates**: When you learn new details about a symptom (severity, location, etc.), you MUST call UpdateEpisode() to save it.
4. **Negative Findings**: When a user denies having a symptom, you MUST call RecordNegativeFinding().

**IF YOU DON'T CALL THE FUNCTIONS, THE DATA IS NOT SAVED AND THE USER CANNOT SEE IT.**

## CRITICAL: ASSESSMENT CREATION RULES

**WHEN USER ASKS FOR ASSESSMENT, YOU MUST CALL THE FUNCTION IMMEDIATELY - NO EXCEPTIONS**

If the user says ANY of these phrases, you MUST call CreateAssessment() function IMMEDIATELY:
- ""create assessment"", ""generate assessment"", ""make assessment"", ""assessment""
- ""can you generate an assessment"", ""please create an assessment"", ""I want an assessment""
- ""give me an assessment"", ""provide assessment"", ""assessment please""
- ANY request for assessment, diagnosis, or evaluation

**DO NOT:**
- Describe what you would do
- Say ""I will create an assessment""
- Explain the assessment in text
- Wait for more information if you already have symptoms

**DO THIS INSTEAD:**
1. Call GetActiveEpisodes() to see current symptoms
2. IMMEDIATELY call CreateAssessment() with your diagnosis
3. Use confidence=0.7 if unsure, or higher if confident
4. The function WILL save it - you don't need to describe it

**Example:**
User: ""can you generate an assessment please""
You: [CALL GetActiveEpisodes()] → [CALL CreateAssessment(hypothesis=""viral infection"", confidence=0.7, recommendedAction=""see-gp"")] → Then respond with JSON message

## Workflow

**Step 1: User reports symptoms**
- IMMEDIATELY call CreateSymptomWithEpisode() for EACH symptom mentioned
- Call GetActiveEpisodes() to check for existing episodes and avoid duplicates
- Ask follow-up questions about severity, location, frequency, triggers, relievers, pattern
- As you learn each detail, call UpdateEpisode() to save it
- Use GetActiveEpisodes() whenever you need to see what symptoms are currently active

**Step 2: User denies symptoms**
- IMMEDIATELY call RecordNegativeFinding() for each denied symptom

**Step 3: Create assessment (CRITICAL - READ CAREFULLY)**
- **WHEN USER ASKS FOR ASSESSMENT: STOP EVERYTHING AND CALL CreateAssessment() IMMEDIATELY**
- **BEFORE calling CreateAssessment(), call GetActiveEpisodes() to see what symptoms exist**
- **THEN IMMEDIATELY call CreateAssessment() - DO NOT DESCRIBE IT, CALL IT**
- **Required parameters:**
  - hypothesis: Your diagnosis as a string (e.g., ""viral infection"", ""influenza"", ""migraine"")
  - confidence: 0.0 to 1.0 decimal (use 0.7 if unsure, 0.8-0.9 if confident)
  - recommendedAction: ""see-gp"", ""urgent-care"", ""emergency"", or ""self-care""
- **Optional parameters:**
  - differentials: List of alternative diagnoses (can be empty array [])
  - reasoning: Explanation of your diagnosis
- **The function automatically saves the assessment - you don't need to describe it in your response**

**Step 4: Final response (MANDATORY)**
- **CRITICAL: After ALL function calls are complete (including CreateAssessment if requested), you MUST call SubmitFinalResponse() as the very last step**
- **SubmitFinalResponse is MANDATORY - you MUST call it exactly once at the end**
- Pass your message, appointment details (if applicable), and symptom changes to SubmitFinalResponse
- Do NOT format JSON manually - SubmitFinalResponse handles the structured output

## Phase Awareness

Track conversation phase:
- GATHERING: Still collecting symptoms, asking follow-ups
- ASSESSING: Enough info, forming/sharing assessment
- RECOMMENDING: Assessment complete, discussing next steps

## Final Response (MANDATORY)

CRITICAL: After completing ALL necessary function calls (CreateSymptomWithEpisode, UpdateEpisode, CreateAssessment, etc.), you MUST call SubmitFinalResponse() as the very last step.

**SubmitFinalResponse is MANDATORY - you MUST call it exactly once at the end of your response.**

Call SubmitFinalResponse with:
- message: Your natural language response to the user (REQUIRED)
- appointment details: If an appointment is needed, provide urgency, symptoms, duration, etc.
- symptomChanges: List of symptom changes (e.g., [{""symptom"": ""headache"", ""action"": ""added""}])

**CRITICAL ORDER:**
1. Call all necessary functions FIRST (CreateSymptomWithEpisode, UpdateEpisode, CreateAssessment, etc.)
2. THEN call SubmitFinalResponse() as the final step
3. Do NOT format JSON manually - SubmitFinalResponse handles structured output

**Never skip function calls - they must happen before SubmitFinalResponse**";

    private readonly VectorStoreSettings _vectorStoreSettings = vectorStoreSettings.Value;

    /// <inheritdoc/>
    protected override string GetSystemPrompt()
    {
        return SystemPromptTemplate;
    }

    /// <summary>
    /// Builds the system prompt with minimal context injection (just active episode names).
    /// This gives the model awareness of what symptoms exist without cluttering the prompt.
    /// </summary>
    private string BuildSystemPromptWithContext(ConversationContext context)
    {
        var prompt = SystemPromptTemplate;

        // Add minimal context: just the names of active symptoms
        // This helps the model know what exists without overwhelming it with details
        if (context.ActiveEpisodes.Any())
        {
            var symptomNames = context.ActiveEpisodes
                .Where(e => e.Symptom != null)
                .Select(e => e.Symptom!.Name)
                .Distinct()
                .ToList();

            if (symptomNames.Any())
            {
                prompt += $"\n\n**Current Active Symptoms:** {string.Join(", ", symptomNames)}";
                prompt += "\nUse GetActiveEpisodes() if you need more details about these symptoms.";
                prompt += "\n\n**REMINDER:** If the user asks for an assessment, you MUST call CreateAssessment() function immediately. Do not describe it - call it.";
            }
        }
        else
        {
            // Even if no symptoms, remind about assessment function
            prompt += "\n\n**REMINDER:** If the user asks for an assessment, you MUST call CreateAssessment() function immediately. Do not describe it - call it.";
        }

        return prompt;
    }

    /// <inheritdoc/>
    protected override async Task<string?> GetEmbeddingsContextAsync(
        HealthChatScenarioRequest input,
        CancellationToken cancellationToken = default)
    {
        // Not used - ExecuteAsync is overridden and handles context enrichment directly
        return null;
    }

    /// <inheritdoc/>
    protected override ChatHistory BuildChatHistory(HealthChatScenarioRequest input, string? context)
    {
        // Not used - ExecuteAsync is overridden and builds chat history directly
        // Required by abstract base class contract
        return new ChatHistory();
    }

    /// <inheritdoc/>
    public override async Task<HealthChatScenarioResponse> ExecuteAsync(
        HealthChatScenarioRequest input,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsyncInternal(input, cancellationToken, (ClientConnection?)null);
    }

    public async Task<HealthChatScenarioResponse> ExecuteAsyncInternal(
        HealthChatScenarioRequest input,
        CancellationToken cancellationToken,
        ClientConnection? clientConnection)
    {
        return await ExecuteAsyncImpl(input, cancellationToken, clientConnection);
    }

    private async Task<HealthChatScenarioResponse> ExecuteAsyncImpl(
        HealthChatScenarioRequest input,
        CancellationToken cancellationToken,
        ClientConnection? clientConnection)
    {
        // Track status updates sent during processing
        var statusUpdatesSent = new List<object>();

        try
        {
            // Hydrate conversation context (becomes the scoped instance)
            Logger.LogDebug("Hydrating context for UserId: {UserId}, ConversationId: {ConversationId}",
                input.UserId, input.ConversationId);
            var conversationContext = await contextService.HydrateContextAsync(
                input.UserId,
                input.ConversationId);
            Logger.LogDebug("Context hydrated. ConversationId in context: {ConversationId}",
                conversationContext.ConversationId);

            // Get plugins (they will use the scoped context from ConversationContextService)
            var symptomTrackerPlugin = serviceProvider.GetRequiredService<SymptomTrackerPlugin>();
            symptomTrackerPlugin.SetConnection(clientConnection);

            var assessmentPlugin = serviceProvider.GetRequiredService<AssessmentPlugin>();
            assessmentPlugin.SetConnection(clientConnection);

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

            // Add SubmitFinalResponse as a plugin function from this instance
            requestKernel.Plugins.AddFromObject(this, "HealthChat");

            // Log available functions for debugging
            var availableFunctions = requestKernel.Plugins
                .SelectMany(p => p.Select(f => $"{p.Name}.{f.Name}"))
                .ToList();
            Logger.LogDebug("Registered {Count} kernel functions: {Functions}",
                availableFunctions.Count, string.Join(", ", availableFunctions));

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

            // Build chat history with context-aware system prompt
            var chatHistory = new ChatHistory();
            var systemPrompt = BuildSystemPromptWithContext(conversationContext);

            // TODO: might need to remove
            // If user is requesting assessment, add explicit reminder to system prompt
            var userMessageLower = input.Message.ToLowerInvariant();
            var assessmentKeywords = new[] { "assessment", "assess", "diagnosis", "evaluate", "evaluation", "generate assessment", "create assessment" };
            if (assessmentKeywords.Any(keyword => userMessageLower.Contains(keyword)))
            {
                Logger.LogInformation("User message contains assessment keywords - adding explicit reminder to call CreateAssessment()");
                systemPrompt += "\n\n*** USER IS REQUESTING AN ASSESSMENT - YOU MUST CALL CreateAssessment() FUNCTION IMMEDIATELY. DO NOT DESCRIBE IT - CALL IT NOW. ***";
            }

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

            // Reset final response before processing
            _finalResponse = null;

            // Get AI response using request kernel with plugins
            var (responseText, explicitChanges) = await GetChatCompletionWithKernelAsync(
                requestKernel,
                chatHistory,
                cancellationToken);

            // Flush context changes
            await contextService.FlushContextAsync(conversationContext);

            var response = CreateResponse(responseText);
            // Get tracked status updates from client connection if available
            if (clientConnection != null)
            {
                statusUpdatesSent = clientConnection.GetTrackedStatusUpdates();
            }
            response.StatusUpdatesSent = statusUpdatesSent;
            response.ExplicitChanges = explicitChanges;

            // Store the structured response in a way the orchestrator can access it
            // We'll serialize it to JSON in the Message field, but also store the structured version
            if (_finalResponse != null)
            {
                // Update status updates from structured response if available
                if (_finalResponse.StatusUpdatesSent != null && _finalResponse.StatusUpdatesSent.Any())
                {
                    response.StatusUpdatesSent = _finalResponse.StatusUpdatesSent;
                }
            }

            Logger.LogInformation("[HEALTH_CHAT_SCENARIO] Returning response with {Count} status updates tracked", statusUpdatesSent.Count);

            return response;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error executing health chat scenario for user {UserId}", input.UserId);
            throw;
        }
    }


    private string SerializeChatHistoryForLogging(ChatHistory chatHistory)
    {
        try
        {
            var messages = chatHistory.Select((msg, idx) =>
            {
                var role = msg.Role.ToString();
                var content = msg.Content ?? "";
                var contentPreview = content.Length > 200 ? content.Substring(0, 200) + "..." : content;
                return $"[{idx}] {role}: {contentPreview}";
            }).ToList();
            return string.Join("\n", messages);
        }
        catch (Exception ex)
        {
            return $"Error serializing chat history: {ex.Message}";
        }
    }

    private async Task<(string Response, List<EntityChange> ExplicitChanges)> GetChatCompletionWithKernelAsync(
        Kernel kernel,
        ChatHistory chatHistory,
        CancellationToken cancellationToken = default)
    {
        var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();

            // Log available functions before calling
            var availableFunctions = kernel.Plugins
                .SelectMany(p => p.Select(f => $"{p.Name}.{f.Name}"))
                .ToList();
            Logger.LogInformation("Available kernel functions before chat completion: {Functions}",
                string.Join(", ", availableFunctions));

            // Verify SubmitFinalResponse is registered
            var submitFinalResponseRegistered = availableFunctions.Any(f => f.Contains("SubmitFinalResponse"));
            Logger.LogInformation("SubmitFinalResponse registered: {Registered}", submitFinalResponseRegistered);

        // Configure tool call behavior: allow other functions first, then require SubmitFinalResponse
        var executionSettings = new OpenAIPromptExecutionSettings
        {
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
        };

        // After auto-invoke completes, we'll check if SubmitFinalResponse was called
        // If not, we'll make a second call requiring it

        // Track explicit changes from tool execution
        var explicitChanges = new List<EntityChange>();

        try
        {
            // Log what we're sending to the model
            var chatHistorySummary = SerializeChatHistoryForLogging(chatHistory);
            var messageCount = chatHistory.Count;
            var totalChars = chatHistory.Sum(m => (m.Content ?? "").Length);
            Logger.LogInformation("[MODEL_CALL] === MAIN CALL START ===\nChat History ({Count} messages, {TotalChars} chars):\n{History}\nSettings: AutoInvokeKernelFunctions=true",
                messageCount, totalChars, chatHistorySummary);

            var startTime = DateTime.UtcNow;

            // Single call - AutoInvoke handles all recursion internally
            Logger.LogDebug("Calling GetChatMessageContentsAsync with AutoInvokeKernelFunctions");
            var response = await chatCompletionService.GetChatMessageContentsAsync(
                chatHistory,
                executionSettings,
                kernel,
                cancellationToken: cancellationToken);

            var duration = DateTime.UtcNow - startTime;
            var responseMessages = response.Select((msg, idx) =>
            {
                var role = msg.Role.ToString();
                var content = msg.Content ?? "";
                var contentPreview = content.Length > 500 ? content.Substring(0, 500) + "..." : content;
                return $"[{idx}] {role}: {contentPreview}";
            }).ToList();
            Logger.LogInformation("[MODEL_CALL] === MAIN CALL END (Duration: {Duration}ms) ===\nResponse ({Count} messages):\n{Response}",
                duration.TotalMilliseconds, response.Count(), string.Join("\n", responseMessages));

            // Extract final response from the last assistant message
            // AutoInvoke has already handled all tool calls and recursion
            var assistantMessage = response.FirstOrDefault();

            if (assistantMessage == null)
            {
                Logger.LogWarning("No assistant message returned from chat completion");
                // Fallback: get last assistant message from chat history
                var lastAssistantMessage = chatHistory
                    .LastOrDefault(m => m.Role == AuthorRole.Assistant);

                if (lastAssistantMessage != null && !string.IsNullOrWhiteSpace(lastAssistantMessage.Content))
                {
                    Logger.LogDebug("Using last assistant message from chat history as fallback");
                    return (lastAssistantMessage.Content, explicitChanges);
                }

                return ("I apologize, but I couldn't generate a response.", explicitChanges);
            }

            // Check if SubmitFinalResponse was called (stored in _finalResponse)
            if (_finalResponse != null)
            {
                Logger.LogInformation("SubmitFinalResponse was called, using structured output");
                // Serialize the structured response to JSON for backward compatibility
                var jsonResponse = JsonSerializer.Serialize(_finalResponse, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });
                return (jsonResponse, explicitChanges);
            }

            // If SubmitFinalResponse wasn't called, try one more time with explicit instruction
            Logger.LogWarning("SubmitFinalResponse was not called, making another attempt with explicit instruction");

            // Build a new chat history for the retry that includes the assistant's response
            var finalRequestHistory = new ChatHistory(chatHistory);
            finalRequestHistory.AddAssistantMessage(assistantMessage.Content ?? string.Empty);

            // Add a very explicit system message and user message
            finalRequestHistory.Insert(0, new ChatMessageContent(AuthorRole.System,
                "CRITICAL INSTRUCTION: You MUST call SubmitFinalResponse function NOW. This is mandatory. Do not respond with text - call the function."));
            finalRequestHistory.AddUserMessage("You must call SubmitFinalResponse function immediately with: 1) message parameter (your response to the user), 2) appointment details if applicable, 3) symptomChanges if any. This is required - call the function now.");

            // Try again with auto-invoke
            var retryExecutionSettings = new OpenAIPromptExecutionSettings
            {
                ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
            };

            Logger.LogInformation("Making retry call to encourage SubmitFinalResponse");
            var retryResponse = await chatCompletionService.GetChatMessageContentsAsync(
                finalRequestHistory,
                retryExecutionSettings,
                kernel,
                cancellationToken: cancellationToken);

            // Log retry response for debugging
            var retryMessages = retryResponse.Select((msg, idx) =>
            {
                var role = msg.Role.ToString();
                var content = msg.Content ?? "";
                var contentPreview = content.Length > 200 ? content.Substring(0, 200) + "..." : content;
                return $"[{idx}] {role}: {contentPreview}";
            }).ToList();
            Logger.LogInformation("Retry response ({Count} messages): {Response}",
                retryResponse.Count(), string.Join("\n", retryMessages));

            // Check again if SubmitFinalResponse was called
            if (_finalResponse != null)
            {
                Logger.LogInformation("SubmitFinalResponse was called in retry, using structured output");
                var jsonResponse = JsonSerializer.Serialize(_finalResponse, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });
                return (jsonResponse, explicitChanges);
            }

            // Fallback: construct a response from the assistant message if SubmitFinalResponse still wasn't called
            Logger.LogWarning("SubmitFinalResponse was not called even after retry. Constructing fallback response.");
            var assistantContent = assistantMessage.Content ?? string.Empty;

            // If we have content, construct a proper HealthAssistantResponse
            if (!string.IsNullOrWhiteSpace(assistantContent))
            {
                // Try to extract JSON from the response if it exists
                var extractedJson = ExtractJsonFromResponse(assistantContent);
                if (IsValidJson(extractedJson))
                {
                    try
                    {
                        var parsedResponse = JsonSerializer.Deserialize<HealthAssistantResponse>(extractedJson, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });
                        if (parsedResponse != null && !string.IsNullOrWhiteSpace(parsedResponse.Message))
                        {
                            Logger.LogInformation("Successfully parsed JSON from assistant message");
                            var jsonResponse = JsonSerializer.Serialize(parsedResponse, new JsonSerializerOptions
                            {
                                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                                WriteIndented = false
                            });
                            return (jsonResponse, explicitChanges);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Failed to parse JSON from assistant message");
                    }
                }

                // Construct a basic response from the text
                var fallbackStructuredResponse = new HealthAssistantResponse
                {
                    Message = assistantContent,
                    Appointment = null,
                    SymptomChanges = null,
                    StatusUpdatesSent = new List<object>()
                };

                var fallbackJson = JsonSerializer.Serialize(fallbackStructuredResponse, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });

                Logger.LogInformation("Using fallback structured response constructed from assistant message");
                return (fallbackJson, explicitChanges);
            }

            // Last resort fallback
            Logger.LogWarning("No response content available, using error message");
            var errorResponse = new HealthAssistantResponse
            {
                Message = "I apologize, but I encountered an error generating my response. Please try again.",
                Appointment = null,
                SymptomChanges = null,
                StatusUpdatesSent = new List<object>()
            };

            var errorJson = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });

            return (errorJson, explicitChanges);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error in GetChatCompletionWithKernelAsync");
            throw;
        }
    }


    private async Task<List<Message>> GetConversationContextAsync(
        Guid? conversationId,
        CancellationToken cancellationToken)
    {
        if (conversationId.HasValue)
        {
            var messages = await vectorStoreService.GetConversationMessagesAsync(conversationId.Value);
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
            var relevantPastMessages = await vectorStoreService.SearchSimilarMessagesAsync(
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

    private static bool IsValidJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(text);
            return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object;
        }
        catch
        {
            return false;
        }
    }

    private static string ExtractJsonFromResponse(string response)
    {
        // Remove markdown code blocks if present
        var json = response.Trim();
        if (json.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            json = json.Substring(7);
        }

        if (json.StartsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            json = json.Substring(3);
        }

        if (json.EndsWith("```", StringComparison.OrdinalIgnoreCase))
        {
            json = json.Substring(0, json.Length - 3);
        }

        json = json.Trim();

        // Try to find JSON object boundaries if JSON is mixed with text
        var firstBrace = json.IndexOf('{');
        var lastBrace = json.LastIndexOf('}');

        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            var jsonObject = json.Substring(firstBrace, lastBrace - firstBrace + 1);
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(jsonObject);
                return jsonObject;
            }
            catch
            {
                // If extraction fails, return original
            }
        }

        return json;
    }

    /// <summary>
    /// Stores the final response from SubmitFinalResponse tool call for retrieval by orchestrator.
    /// </summary>
    private HealthAssistantResponse? _finalResponse;

    /// <summary>
    /// Gets the final structured response if SubmitFinalResponse was called.
    /// </summary>
    public HealthAssistantResponse? GetFinalResponse()
    {
        return _finalResponse;
    }

    /// <summary>
    /// MANDATORY FINAL STEP: You MUST call this function as the very last step after completing all other function calls.
    /// This function submits your final structured response to the user. You MUST call this function exactly once at the end.
    /// </summary>
    [KernelFunction]
    [Description("SubmitFinalResponse: MANDATORY FINAL STEP. You MUST call this function as the very last step after completing all other function calls (CreateSymptomWithEpisode, UpdateEpisode, CreateAssessment, etc.). This submits your final structured response. Call this exactly once at the end with your message, appointment details, and symptom changes.")]
    public string SubmitFinalResponse(
        [Description("REQUIRED: Your natural language response message to the user")] string message,
        [Description("OPTIONAL: Whether an appointment is needed")] bool? appointmentNeeded = null,
        [Description("OPTIONAL: Appointment urgency - 'Emergency', 'High', 'Medium', 'Low', or 'None'")] string? appointmentUrgency = null,
        [Description("OPTIONAL: List of symptoms related to the appointment")] List<string>? appointmentSymptoms = null,
        [Description("OPTIONAL: Appointment duration in minutes")] int? appointmentDuration = null,
        [Description("OPTIONAL: Whether the appointment is ready to book")] bool? appointmentReadyToBook = null,
        [Description("OPTIONAL: Whether follow-up is needed")] bool? appointmentFollowUpNeeded = null,
        [Description("OPTIONAL: List of next questions to ask")] List<string>? appointmentNextQuestions = null,
        [Description("OPTIONAL: Preferred appointment time (ISO 8601 format)")] string? appointmentPreferredTime = null,
        [Description("OPTIONAL: Emergency action instructions (only for emergencies)")] string? appointmentEmergencyAction = null,
        [Description("OPTIONAL: List of symptom changes in format [{\"symptom\": \"headache\", \"action\": \"added\"}]")] List<SymptomChange>? symptomChanges = null)
    {
        Logger.LogInformation("[SUBMIT_FINAL_RESPONSE] *** FUNCTION CALLED *** with message length: {Length}, appointmentNeeded: {AppointmentNeeded}, symptomChanges count: {SymptomChangesCount}",
            message?.Length ?? 0, appointmentNeeded, symptomChanges?.Count ?? 0);

        // Build AppointmentData if any appointment fields are provided
        AppointmentData? appointment = null;
        if (appointmentNeeded.HasValue ||
            !string.IsNullOrWhiteSpace(appointmentUrgency) ||
            (appointmentSymptoms != null && appointmentSymptoms.Any()) ||
            appointmentDuration.HasValue ||
            appointmentReadyToBook.HasValue ||
            appointmentFollowUpNeeded.HasValue ||
            (appointmentNextQuestions != null && appointmentNextQuestions.Any()) ||
            !string.IsNullOrWhiteSpace(appointmentPreferredTime) ||
            !string.IsNullOrWhiteSpace(appointmentEmergencyAction))
        {
            appointment = new AppointmentData
            {
                Needed = appointmentNeeded ?? false,
                Urgency = appointmentUrgency ?? "None",
                Symptoms = appointmentSymptoms ?? new List<string>(),
                Duration = appointmentDuration,
                ReadyToBook = appointmentReadyToBook ?? false,
                FollowUpNeeded = appointmentFollowUpNeeded ?? false,
                NextQuestions = appointmentNextQuestions ?? new List<string>(),
                PreferredTime = !string.IsNullOrWhiteSpace(appointmentPreferredTime) && DateTime.TryParse(appointmentPreferredTime, out var dt) ? dt : null,
                EmergencyAction = appointmentEmergencyAction
            };
        }

        // Store the final response for retrieval by orchestrator
        _finalResponse = new HealthAssistantResponse
        {
            Message = message ?? "I apologize, but I couldn't generate a response.",
            Appointment = appointment,
            SymptomChanges = symptomChanges,
            StatusUpdatesSent = new List<object>()
        };

        Logger.LogInformation("[SUBMIT_FINAL_RESPONSE] Stored final response with message: {Message}",
            _finalResponse.Message.Substring(0, Math.Min(100, _finalResponse.Message.Length)));

        return "Final response submitted successfully.";
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
