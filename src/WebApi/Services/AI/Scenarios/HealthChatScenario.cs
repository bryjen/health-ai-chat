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
    ClientConnectionService clientConnectionService,
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

**Step 4: Final response**
- **IMPORTANT: Function calls happen FIRST, then JSON formatting**
- After ALL function calls are complete (including CreateAssessment if requested), format your final response as JSON
- The JSON ""message"" field should acknowledge what you did (e.g., ""I've created an assessment based on your symptoms"")

## Phase Awareness

Track conversation phase:
- GATHERING: Still collecting symptoms, asking follow-ups
- ASSESSING: Enough info, forming/sharing assessment
- RECOMMENDING: Assessment complete, discussing next steps

## Response Format

CRITICAL: Function calls happen FIRST, then JSON formatting. Do NOT skip function calls to format JSON faster.

After you have completed ALL necessary function calls (especially CreateAssessment if user requested it), format your final response as valid JSON in this EXACT format:
{
  ""message"": ""Your response message to the user - THIS FIELD IS REQUIRED"",
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

REQUIREMENTS:
- The ""message"" field is REQUIRED and must be a string (not an object or array)
- The ""message"" field must contain your natural language response to the user
- Do NOT include other fields at the root level (like ""symptoms"" or ""questions"") - only ""message"", ""appointment"", and ""symptomChanges""
- **CRITICAL ORDER: 1) Call functions FIRST (especially CreateAssessment if requested), 2) THEN format JSON response**
- **If user asks for assessment, you MUST call CreateAssessment() BEFORE formatting your JSON response**
- **Never skip function calls - they must happen before JSON formatting**";

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

            // Set client connection for plugins to access
            clientConnectionService.CurrentConnection = clientConnection;

            // Get plugins (they will use the scoped context from ConversationContextService)
            var symptomTrackerPlugin = serviceProvider.GetRequiredService<SymptomTrackerPlugin>();
            var assessmentPlugin = serviceProvider.GetRequiredService<AssessmentPlugin>();

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

        var executionSettings = new OpenAIPromptExecutionSettings
        {
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
        };

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

            var finalResponse = assistantMessage.Content ?? string.Empty;

            if (string.IsNullOrWhiteSpace(finalResponse))
            {
                Logger.LogWarning("Assistant message has no content, checking chat history");
                // Fallback: get last assistant message from chat history
                var lastAssistantMessage = chatHistory
                    .LastOrDefault(m => m.Role == AuthorRole.Assistant);

                if (lastAssistantMessage != null && !string.IsNullOrWhiteSpace(lastAssistantMessage.Content))
                {
                    finalResponse = lastAssistantMessage.Content;
                }
                else
                {
                    finalResponse = "I apologize, but I couldn't generate a response.";
                }
            }

            // Post-process: Ensure response is valid JSON format
            // If the response is not valid JSON, try to format it
            finalResponse = await EnsureJsonFormatAsync(
                finalResponse,
                chatCompletionService,
                chatHistory,
                kernel,
                cancellationToken);

            // Validate that response has required "message" field
            finalResponse = await ValidateAndFixMessageFieldAsync(
                finalResponse,
                chatCompletionService,
                kernel,
                cancellationToken);

            Logger.LogDebug("Final response generated (length: {Length})", finalResponse.Length);
            return (finalResponse, explicitChanges);
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

    private async Task<string> EnsureJsonFormatAsync(
        string response,
        IChatCompletionService chatCompletionService,
        ChatHistory chatHistory,
        Kernel kernel,
        CancellationToken cancellationToken)
    {
        // Check if response is already valid JSON
        if (IsValidJson(response))
        {
            Logger.LogDebug("Response is already valid JSON, no formatting needed");
            return response;
        }

        // Try to extract JSON from response (might be wrapped in markdown)
        var extractedJson = ExtractJsonFromResponse(response);
        if (IsValidJson(extractedJson))
        {
            Logger.LogDebug("Extracted valid JSON from response");
            return extractedJson;
        }

        // If not valid JSON, ask AI to format it
        Logger.LogDebug("Response is not valid JSON, requesting formatting");
        try
        {
            var formattingHistory = new ChatHistory();
            var schemaPrompt = @"You are a JSON formatter. Format the following response as valid JSON according to this EXACT schema:

{
  ""message"": ""string (REQUIRED - your response to the user)"",
  ""appointment"": {
    ""needed"": boolean,
    ""urgency"": ""Emergency"" | ""High"" | ""Medium"" | ""Low"" | ""None"",
    ""symptoms"": [""string""],
    ""duration"": number,
    ""readyToBook"": boolean,
    ""followUpNeeded"": boolean,
    ""nextQuestions"": [""string""],
    ""preferredTime"": null,
    ""emergencyAction"": ""string""
  },
  ""symptomChanges"": [
    {""symptom"": ""string"", ""action"": ""string""}
  ]
}

CRITICAL: The ""message"" field is REQUIRED and must be a string. If the response contains symptoms or questions but no message field, create an appropriate message string that includes that information.";
            formattingHistory.AddSystemMessage(schemaPrompt);
            formattingHistory.AddUserMessage($"Format this response as valid JSON with a REQUIRED 'message' field:\n\n{response}");

            // Log what we're sending to the model for formatting
            var formattingHistorySummary = SerializeChatHistoryForLogging(formattingHistory);
            Logger.LogInformation("[MODEL_CALL] === FORMATTING CALL START ===\nChat History ({Count} messages):\n{History}\nSettings: No tool calling",
                formattingHistory.Count, formattingHistorySummary);

            var formattingStartTime = DateTime.UtcNow;

            // Create formatting settings without tool calling
            // Use default behavior (no tools) for formatting pass
            var formattingSettings = new OpenAIPromptExecutionSettings();

            var formattedResponse = await chatCompletionService.GetChatMessageContentsAsync(
                formattingHistory,
                formattingSettings,
                kernel,
                cancellationToken: cancellationToken);

            var formattingDuration = DateTime.UtcNow - formattingStartTime;
            var formattedResponseMessages = formattedResponse.Select((msg, idx) =>
            {
                var role = msg.Role.ToString();
                var content = msg.Content ?? "";
                var contentPreview = content.Length > 500 ? content.Substring(0, 500) + "..." : content;
                return $"[{idx}] {role}: {contentPreview}";
            }).ToList();
            Logger.LogInformation("[MODEL_CALL] === FORMATTING CALL END (Duration: {Duration}ms) ===\nResponse ({Count} messages):\n{Response}",
                formattingDuration.TotalMilliseconds, formattedResponse.Count(), string.Join("\n", formattedResponseMessages));

            var formattedMessage = formattedResponse.FirstOrDefault()?.Content ?? response;

            // Try to extract JSON from formatted response
            var formattedJson = ExtractJsonFromResponse(formattedMessage);
            if (IsValidJson(formattedJson))
            {
                Logger.LogDebug("Successfully formatted response as JSON");
                return formattedJson;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error formatting response as JSON, returning original");
        }

        // Fallback: return original response
        Logger.LogWarning("Could not format response as JSON, returning original");
        return response;
    }

    private async Task<string> ValidateAndFixMessageFieldAsync(
        string response,
        IChatCompletionService chatCompletionService,
        Kernel kernel,
        CancellationToken cancellationToken)
    {
        // Check if response is valid JSON and has "message" field
        try
        {
            var jsonText = ExtractJsonFromResponse(response);
            if (!IsValidJson(jsonText))
            {
                return response; // Not valid JSON, return as-is (will be handled by parser)
            }

            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            // Check for message field (case-insensitive)
            bool hasMessageField = false;
            foreach (var prop in root.EnumerateObject())
            {
                if (string.Equals(prop.Name, "message", StringComparison.OrdinalIgnoreCase))
                {
                    // Check if it's a string (not object/array)
                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        var messageValue = prop.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(messageValue))
                        {
                            hasMessageField = true;
                            break;
                        }
                    }
                }
            }

            if (hasMessageField)
            {
                Logger.LogDebug("Response has valid message field");
                return response;
            }

            // Missing message field - request reformatting with stronger prompt
            Logger.LogWarning("Response is missing required 'message' field, requesting reformatting");
            var validationHistory = new ChatHistory();
            var validationPrompt = @"You are a JSON formatter. The following JSON response is missing the REQUIRED ""message"" field.

You MUST add a ""message"" field that is a STRING (not an object or array) containing a natural language response to the user.

If the JSON contains symptoms, questions, or other data, convert that information into a readable message string in the ""message"" field.

The response MUST have this structure:
{
  ""message"": ""string (REQUIRED - your response to the user)"",
  ...other fields...
}

Return ONLY the corrected JSON, nothing else.";
            validationHistory.AddSystemMessage(validationPrompt);
            validationHistory.AddUserMessage($"Add the required 'message' field to this JSON:\n\n{response}");

            // Log what we're sending to the model for validation
            var validationHistorySummary = SerializeChatHistoryForLogging(validationHistory);
            Logger.LogInformation("[MODEL_CALL] === VALIDATION CALL START ===\nChat History ({Count} messages):\n{History}\nSettings: No tool calling",
                validationHistory.Count, validationHistorySummary);

            var validationStartTime = DateTime.UtcNow;

            var validationSettings = new OpenAIPromptExecutionSettings();
            var validatedResponse = await chatCompletionService.GetChatMessageContentsAsync(
                validationHistory,
                validationSettings,
                kernel,
                cancellationToken: cancellationToken);

            var validationDuration = DateTime.UtcNow - validationStartTime;
            var validatedResponseMessages = validatedResponse.Select((msg, idx) =>
            {
                var role = msg.Role.ToString();
                var content = msg.Content ?? "";
                var contentPreview = content.Length > 500 ? content.Substring(0, 500) + "..." : content;
                return $"[{idx}] {role}: {contentPreview}";
            }).ToList();
            Logger.LogInformation("[MODEL_CALL] === VALIDATION CALL END (Duration: {Duration}ms) ===\nResponse ({Count} messages):\n{Response}",
                validationDuration.TotalMilliseconds, validatedResponse.Count(), string.Join("\n", validatedResponseMessages));

            var validatedMessage = validatedResponse.FirstOrDefault()?.Content ?? response;
            var validatedJson = ExtractJsonFromResponse(validatedMessage);

            if (IsValidJson(validatedJson))
            {
                // Verify it now has message field
                using var validatedDoc = JsonDocument.Parse(validatedJson);
                var validatedRoot = validatedDoc.RootElement;
                foreach (var prop in validatedRoot.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "message", StringComparison.OrdinalIgnoreCase) &&
                        prop.Value.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(prop.Value.GetString()))
                    {
                        Logger.LogDebug("Successfully added message field to response");
                        return validatedJson;
                    }
                }
            }

            Logger.LogWarning("Could not add message field, returning original response");
            return response;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Error validating message field, returning original response");
            return response;
        }
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

    protected override HealthChatScenarioResponse CreateResponse(string responseText)
    {
        return new HealthChatScenarioResponse
        {
            Message = responseText,
            StatusUpdatesSent = new List<object>()
        };
    }
}
