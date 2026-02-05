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

            // Check if user requested assessment (used for both system prompt and retry logic)
            var userMessageLower = input.Message.ToLowerInvariant();
            var assessmentKeywords = new[] { "assessment", "assess", "diagnosis", "evaluate", "evaluation", "generate assessment", "create assessment" };
            var assessmentRequested = assessmentKeywords.Any(keyword => userMessageLower.Contains(keyword));

            // If user is requesting assessment, add explicit reminder to system prompt
            if (assessmentRequested)
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

            // Store assessment state before call to detect if one was created
            var assessmentIdBefore = conversationContext.CurrentAssessment?.Id;

            // Reset final response before processing
            _finalResponse = null;

            // Get AI response using request kernel with plugins
            var (responseText, explicitChanges) = await GetChatCompletionWithKernelAsync(
                requestKernel,
                chatHistory,
                assessmentRequested,
                assessmentIdBefore,
                conversationContext,
                clientConnection,
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



    private async Task<(string Response, List<EntityChange> ExplicitChanges)> GetChatCompletionWithKernelAsync(
        Kernel kernel,
        ChatHistory chatHistory,
        bool assessmentRequested,
        int? assessmentIdBefore,
        ConversationContext conversationContext,
        ClientConnection? clientConnection,
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
            var messageCount = chatHistory.Count;
            var totalChars = chatHistory.Sum(m => (m.Content ?? "").Length);
            var chatHistoryRaw = string.Join("\n", chatHistory.Select((msg, idx) => 
                $"[{idx}] {msg.Role}: {msg.Content ?? ""}"));
            Logger.LogInformation("[MODEL_CALL] === MAIN CALL START ===\nChat History ({Count} messages, {TotalChars} chars):\n{History}\nSettings: AutoInvokeKernelFunctions=true",
                messageCount, totalChars, chatHistoryRaw);

            var startTime = DateTime.UtcNow;

            // Single call - AutoInvoke handles all recursion internally
            Logger.LogDebug("Calling GetChatMessageContentsAsync with AutoInvokeKernelFunctions");
            var response = await chatCompletionService.GetChatMessageContentsAsync(
                chatHistory,
                executionSettings,
                kernel,
                cancellationToken: cancellationToken);

            var duration = DateTime.UtcNow - startTime;
            var responseMessagesRaw = string.Join("\n", response.Select((msg, idx) => 
                $"[{idx}] {msg.Role}: {msg.Content ?? ""}"));
            Logger.LogInformation("[MODEL_CALL] === MAIN CALL END (Duration: {Duration}ms) ===\nResponse ({Count} messages):\n{Response}",
                duration.TotalMilliseconds, response.Count(), responseMessagesRaw);

            // Extract final response from the last assistant message
            // AutoInvoke has already handled all tool calls and recursion
            var assistantMessage = response.FirstOrDefault();
            var assistantContent = assistantMessage?.Content ?? string.Empty;

            // Check if response is empty - this often indicates the model tried to call functions but failed
            if (assistantMessage == null || string.IsNullOrWhiteSpace(assistantContent))
            {
                Logger.LogWarning("Empty or null assistant message returned from chat completion - this may indicate function calling issues");
                Logger.LogInformation("Assistant message (raw): null or empty");

                // If we have a final response from SubmitFinalResponse, use that even if assistant message is empty
                if (_finalResponse != null)
                {
                    Logger.LogInformation("Using SubmitFinalResponse despite empty assistant message");
                    
                    // Merge tracked status updates from client connection if available
                    var trackedStatusUpdatesEmptyMsg = clientConnection?.GetTrackedStatusUpdates() ?? new List<object>();
                    if (trackedStatusUpdatesEmptyMsg.Any())
                    {
                        Logger.LogInformation("Merging {Count} tracked status updates into SubmitFinalResponse (empty message case)", trackedStatusUpdatesEmptyMsg.Count);
                        var existingStatusUpdates = _finalResponse.StatusUpdatesSent ?? new List<object>();
                        var mergedStatusUpdates = existingStatusUpdates.Concat(trackedStatusUpdatesEmptyMsg).ToList();
                        _finalResponse.StatusUpdatesSent = mergedStatusUpdates;
                    }
                    
                    var jsonResponse = JsonSerializer.Serialize(_finalResponse, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = false
                    });
                    Logger.LogInformation("SubmitFinalResponse JSON (raw): {Json}", jsonResponse);
                    return (jsonResponse, explicitChanges);
                }

                // If assessment was requested but not created, or SubmitFinalResponse wasn't called, proceed to retry
                // Don't return early - let the retry logic handle it
                if (!assessmentRequested && _finalResponse == null)
                {
                    // Fallback: get last assistant message from chat history
                    var lastAssistantMessage = chatHistory
                        .LastOrDefault(m => m.Role == AuthorRole.Assistant);

                    // Get tracked status updates from client connection if available
                    var trackedStatusUpdatesEarlyFallback = clientConnection?.GetTrackedStatusUpdates() ?? new List<object>();
                    Logger.LogInformation("Including {Count} tracked status updates in early fallback response", trackedStatusUpdatesEarlyFallback.Count);
                    
                    if (lastAssistantMessage != null && !string.IsNullOrWhiteSpace(lastAssistantMessage.Content))
                    {
                        Logger.LogInformation("Using last assistant message from chat history as fallback. Content (raw): {Content}", lastAssistantMessage.Content);
                        // Always return JSON, not plain text
                        var fallbackResponseFromHistory = new HealthAssistantResponse
                        {
                            Message = lastAssistantMessage.Content,
                            Appointment = null,
                            SymptomChanges = null,
                            StatusUpdatesSent = trackedStatusUpdatesEarlyFallback
                        };
                        var fallbackJsonFromHistory = JsonSerializer.Serialize(fallbackResponseFromHistory, new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                            WriteIndented = false
                        });
                        return (fallbackJsonFromHistory, explicitChanges);
                    }

                    var errorResponse = new HealthAssistantResponse
                    {
                        Message = "I apologize, but I couldn't generate a response.",
                        Appointment = null,
                        SymptomChanges = null,
                        StatusUpdatesSent = trackedStatusUpdatesEarlyFallback
                    };
                    var errorJson = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = false
                    });
                    return (errorJson, explicitChanges);
                }
                // Otherwise, continue to retry logic below
            }

            // Check if assessment was requested and if it was created
            // An assessment was created if CurrentAssessment exists and has a different ID than before
            var assessmentIdAfter = conversationContext.CurrentAssessment?.Id;
            var assessmentCreated = assessmentRequested &&
                assessmentIdAfter.HasValue &&
                assessmentIdAfter.Value != (assessmentIdBefore ?? 0);
            var assessmentMissing = assessmentRequested && !assessmentCreated;

            // Check if SubmitFinalResponse was called (stored in _finalResponse)
            var submitFinalResponseCalled = _finalResponse != null;

            if (submitFinalResponseCalled && !assessmentMissing)
            {
                Logger.LogInformation("SubmitFinalResponse was called, using structured output");
                
                // Merge tracked status updates from client connection if available
                var trackedStatusUpdatesNormal = clientConnection?.GetTrackedStatusUpdates() ?? new List<object>();
                if (trackedStatusUpdatesNormal.Any())
                {
                    Logger.LogInformation("Merging {Count} tracked status updates into SubmitFinalResponse", trackedStatusUpdatesNormal.Count);
                    // Merge with existing status updates, avoiding duplicates
                    var existingStatusUpdates = _finalResponse.StatusUpdatesSent ?? new List<object>();
                    var mergedStatusUpdates = existingStatusUpdates.Concat(trackedStatusUpdatesNormal).ToList();
                    _finalResponse.StatusUpdatesSent = mergedStatusUpdates;
                }
                
                // Serialize the structured response to JSON for backward compatibility
                var jsonResponse = JsonSerializer.Serialize(_finalResponse, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = false
                });
                Logger.LogInformation("SubmitFinalResponse JSON (raw): {Json}", jsonResponse);
                return (jsonResponse, explicitChanges);
            }

            // Determine what needs to be fixed
            var needsRetry = !submitFinalResponseCalled || assessmentMissing;
            if (needsRetry)
            {
                var issues = new List<string>();
                if (assessmentMissing)
                {
                    Logger.LogWarning("Assessment was requested but CreateAssessment() was not called");
                    issues.Add("CreateAssessment");
                }
                if (!submitFinalResponseCalled)
                {
                    Logger.LogWarning("SubmitFinalResponse was not called");
                    issues.Add("SubmitFinalResponse");
                }

                Logger.LogWarning("Making retry call to fix: {Issues}", string.Join(", ", issues));

                // Build a new chat history for the retry that includes the assistant's response (if any)
                var retryHistory = new ChatHistory(chatHistory);
                if (!string.IsNullOrWhiteSpace(assistantContent))
                {
                    retryHistory.AddAssistantMessage(assistantContent);
                }

                // Build explicit retry instructions
                var retrySystemMessage = "CRITICAL INSTRUCTIONS - YOU MUST FOLLOW THESE STEPS IN ORDER. DO NOT SKIP ANY STEP:\n\n";
                var retryUserMessage = "You MUST complete these steps in order. Call the functions - do not describe them:\n\n";

                var stepNumber = 1;
                if (assessmentMissing)
                {
                    retrySystemMessage += $"{stepNumber}. FIRST: Call GetActiveEpisodes() to see current symptoms.\n";
                    stepNumber++;
                    retrySystemMessage += $"{stepNumber}. THEN: IMMEDIATELY call CreateAssessment() function with these REQUIRED parameters:\n";
                    retrySystemMessage += "   - hypothesis: Your diagnosis as a string (e.g., 'viral infection', 'influenza', 'musculoskeletal injury')\n";
                    retrySystemMessage += "   - confidence: 0.7 (decimal between 0.0 and 1.0)\n";
                    retrySystemMessage += "   - recommendedAction: 'see-gp' (or 'urgent-care'/'emergency'/'self-care' if needed)\n";
                    retrySystemMessage += "   - differentials: [] (can be empty array)\n";
                    retrySystemMessage += "   - reasoning: Brief explanation of your diagnosis\n";
                    retrySystemMessage += "   Example: CreateAssessment(hypothesis=\"viral infection\", confidence=0.7, recommendedAction=\"see-gp\", differentials=[], reasoning=\"Based on symptoms of fever, cough, and body soreness\")\n\n";

                    retryUserMessage += $"STEP {stepNumber - 1}: Call GetActiveEpisodes()\n";
                    retryUserMessage += $"STEP {stepNumber}: Call CreateAssessment(hypothesis=\"your diagnosis\", confidence=0.7, recommendedAction=\"see-gp\")\n\n";
                }

                if (!submitFinalResponseCalled)
                {
                    retrySystemMessage += $"{stepNumber}. FINALLY: Call SubmitFinalResponse() function with:\n";
                    retrySystemMessage += "   - message: Your natural language response to the user (REQUIRED)\n";
                    retrySystemMessage += "   - appointment details if applicable (optional)\n";
                    retrySystemMessage += "   - symptomChanges if any (optional)\n";
                    retrySystemMessage += "   Example: SubmitFinalResponse(message=\"I have created an assessment for you. Based on your symptoms...\", ...)\n\n";

                    retryUserMessage += $"STEP {stepNumber}: Call SubmitFinalResponse(message=\"your response message\", ...)\n";
                }

                retrySystemMessage += "\n**CRITICAL: You MUST call these functions in this exact order. DO NOT respond with text-only messages. CALL THE FUNCTIONS.**";
                retryUserMessage += "\n\nCall these functions NOW in the order specified above. Do not describe what you will do - call the functions immediately.";

                retryHistory.Insert(0, new ChatMessageContent(AuthorRole.System, retrySystemMessage));
                retryHistory.AddUserMessage(retryUserMessage);

                // Try again with auto-invoke
                var retryExecutionSettings = new OpenAIPromptExecutionSettings
                {
                    ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
                };

                var retryResponse = await chatCompletionService.GetChatMessageContentsAsync(
                    retryHistory,
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

                // Check if assessment was created in retry
                var assessmentIdAfterRetry = conversationContext.CurrentAssessment?.Id;
                var assessmentCreatedAfterRetry = assessmentRequested &&
                    assessmentIdAfterRetry.HasValue &&
                    assessmentIdAfterRetry.Value != (assessmentIdBefore ?? 0);

                // Check again if SubmitFinalResponse was called
                if (_finalResponse != null)
                {
                    if (assessmentMissing && !assessmentCreatedAfterRetry)
                    {
                        Logger.LogWarning("Assessment was still not created after retry");
                    }
                    else
                    {
                        Logger.LogInformation("SubmitFinalResponse was called in retry, using structured output");
                    }
                    
                    // Merge tracked status updates from client connection if available
                    var trackedStatusUpdatesRetry = clientConnection?.GetTrackedStatusUpdates() ?? new List<object>();
                    if (trackedStatusUpdatesRetry.Any())
                    {
                        Logger.LogInformation("Merging {Count} tracked status updates into retry SubmitFinalResponse", trackedStatusUpdatesRetry.Count);
                        var existingStatusUpdates = _finalResponse.StatusUpdatesSent ?? new List<object>();
                        var mergedStatusUpdates = existingStatusUpdates.Concat(trackedStatusUpdatesRetry).ToList();
                        _finalResponse.StatusUpdatesSent = mergedStatusUpdates;
                    }
                    
                    var jsonResponse = JsonSerializer.Serialize(_finalResponse, new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        WriteIndented = false
                    });
                    Logger.LogInformation("Retry SubmitFinalResponse JSON (raw): {Json}", jsonResponse);
                    return (jsonResponse, explicitChanges);
                }
            }

            // Fallback: construct a response from the assistant message if SubmitFinalResponse still wasn't called
            Logger.LogWarning("SubmitFinalResponse was not called even after retry. Constructing fallback response.");
            Logger.LogInformation("Assistant content (raw): {Content}", assistantContent ?? "");
            
            // Get tracked status updates from client connection if available
            var trackedStatusUpdates = clientConnection?.GetTrackedStatusUpdates() ?? new List<object>();
            Logger.LogInformation("Including {Count} tracked status updates in fallback response", trackedStatusUpdates.Count);
            
            // Always construct a proper JSON response, even if content is plain text
            var fallbackStructuredResponse = new HealthAssistantResponse
            {
                Message = assistantContent ?? "I apologize, but I encountered an error generating my response.",
                Appointment = null,
                SymptomChanges = null,
                StatusUpdatesSent = trackedStatusUpdates
            };

            var fallbackJson = JsonSerializer.Serialize(fallbackStructuredResponse, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });

            Logger.LogInformation("Using fallback structured response. JSON: {Json}", fallbackJson);
            return (fallbackJson, explicitChanges);
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

        var storedJson = JsonSerializer.Serialize(_finalResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });
        Logger.LogInformation("[SUBMIT_FINAL_RESPONSE] Stored final response JSON (raw): {Json}", storedJson);

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
