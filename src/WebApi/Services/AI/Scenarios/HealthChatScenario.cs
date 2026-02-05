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

## Workflow

**Step 1: User reports symptoms**
- IMMEDIATELY call CreateSymptomWithEpisode() for EACH symptom mentioned
- Then call GetActiveEpisodes() to check for existing episodes
- Ask follow-up questions about severity, location, frequency, triggers, relievers, pattern
- As you learn each detail, call UpdateEpisode() to save it

**Step 2: User denies symptoms**
- IMMEDIATELY call RecordNegativeFinding() for each denied symptom

**Step 3: Create assessment**
- **CALL CreateAssessment() IMMEDIATELY when user says:**
  - ""create assessment"", ""regenerate assessment"", ""make assessment"", ""assessment""
  - ""call createassessment"", ""call createassessment function""
  - OR when you have enough info for a diagnosis
- **DO NOT describe assessments in text - CALL THE FUNCTION**
- **Example call:** CreateAssessment(hypothesis=""acute pharyngitis"", confidence=0.75, recommendedAction=""see-gp"")
- Required: hypothesis (your diagnosis as a string), confidence (0.0-1.0 decimal, use 0.7 if unsure)
- Optional: differentials (comma-separated alternatives), reasoning (explanation), recommendedAction (defaults to ""see-gp"")
- The function will save the assessment - you don't need to describe it, just call it

**Step 4: Final response**
- After all function calls are complete, format your final response as JSON with a ""message"" field

## Phase Awareness

Track conversation phase:
- GATHERING: Still collecting symptoms, asking follow-ups
- ASSESSING: Enough info, forming/sharing assessment
- RECOMMENDING: Assessment complete, discussing next steps

## Response Format

CRITICAL: You MUST format your final response as valid JSON with a REQUIRED ""message"" field. The ""message"" field is MANDATORY and must be a string containing your response to the user.

After you have completed all necessary function calls and gathered all information, format your final response as valid JSON in this EXACT format:
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
- Focus on calling functions and gathering information first. Format your final response as JSON only after all function calls are complete.";

    private readonly VectorStoreSettings _vectorStoreSettings = vectorStoreSettings.Value;

    /// <inheritdoc/>
    protected override string GetSystemPrompt()
    {
        return SystemPromptTemplate;
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
            // Hydrate conversation context
            var conversationContext = await contextService.HydrateContextAsync(
                input.UserId,
                input.ConversationId);

            // Create plugins and set their context
            var symptomTrackerPlugin = serviceProvider.GetRequiredService<SymptomTrackerPlugin>();
            symptomTrackerPlugin.SetContext(conversationContext, input.UserId, clientConnection);

            var assessmentPlugin = serviceProvider.GetRequiredService<AssessmentPlugin>();
            if (input.ConversationId.HasValue)
            {
                assessmentPlugin.SetContext(conversationContext, input.UserId, input.ConversationId.Value, clientConnection);
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
            prompt +=
                $"\n\nCurrent assessment: {context.CurrentAssessment.Hypothesis} (confidence: {context.CurrentAssessment.Confidence:P0})";
        }

        return prompt;
    }

    private async Task<(string Response, List<EntityChange> ExplicitChanges)> GetChatCompletionWithKernelAsync(
        Kernel kernel,
        ChatHistory chatHistory,
        CancellationToken cancellationToken = default)
    {
        var chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
        var executionSettings = new OpenAIPromptExecutionSettings
        {
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
        };

        // Track explicit changes from tool execution
        var explicitChanges = new List<EntityChange>();

        try
        {
            // Single call - AutoInvoke handles all recursion internally
            Logger.LogDebug("Calling GetChatMessageContentsAsync with AutoInvokeKernelFunctions");
            var response = await chatCompletionService.GetChatMessageContentsAsync(
                chatHistory,
                executionSettings,
                kernel,
                cancellationToken: cancellationToken);

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

            // Create formatting settings without tool calling
            // Use default behavior (no tools) for formatting pass
            var formattingSettings = new OpenAIPromptExecutionSettings();

            var formattedResponse = await chatCompletionService.GetChatMessageContentsAsync(
                formattingHistory,
                formattingSettings,
                kernel,
                cancellationToken: cancellationToken);

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

            var validationSettings = new OpenAIPromptExecutionSettings();
            var validatedResponse = await chatCompletionService.GetChatMessageContentsAsync(
                validationHistory,
                validationSettings,
                kernel,
                cancellationToken: cancellationToken);

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
