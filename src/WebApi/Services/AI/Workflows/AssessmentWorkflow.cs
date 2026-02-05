using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using WebApi.Hubs;
using WebApi.Services.AI.Tools;
using WebApi.Services.Chat.Conversations;

namespace WebApi.Services.AI.Workflows;

/// <summary>
/// Deterministic workflow for assessment creation.
/// Ensures CreateAssessment → CompleteAssessment always executes in order.
/// </summary>
public class AssessmentWorkflow(
    AssessmentTools assessmentTools,
    SymptomTrackerTools symptomTrackerTools,
    ConversationContextService contextService,
    IChatClient chatClient,
    ILogger<AssessmentWorkflow> logger)
{
    private ClientConnection? _clientConnection;

    public void SetConnection(ClientConnection? clientConnection)
    {
        _clientConnection = clientConnection;
        assessmentTools.SetConnection(clientConnection);
        symptomTrackerTools.SetConnection(clientConnection);
    }

    /// <summary>
    /// Executes the assessment workflow with deterministic flow.
    /// </summary>
    public async Task<WorkflowResult> ExecuteAsync(string userMessage, ConversationContext context)
    {
        var state = new Dictionary<string, object>
        {
            [WorkflowStateKeys.UserId] = context.UserId,
            [WorkflowStateKeys.ConversationId] = context.ConversationId ?? Guid.Empty,
            [WorkflowStateKeys.UserMessage] = userMessage
        };

        try
        {
            // Step 1: Classify intent
            var intent = await ClassifyIntentAsync(userMessage);
            state[WorkflowStateKeys.Intent] = intent;
            logger.LogInformation("Classified intent: {Intent}", intent);

            if (intent != "create_assessment")
            {
                // Not an assessment request, skip to response generation
                var response = await GenerateFinalResponseAsync(state, context);
                state[WorkflowStateKeys.Response] = response;
                return new WorkflowResult { Success = true, State = state, Response = response };
            }

            // Step 2: Extract symptoms using LLM
            var symptomData = await ExtractSymptomsWithLLMAsync(userMessage, context);
            state[WorkflowStateKeys.Symptoms] = symptomData;
            logger.LogInformation("Extracted symptoms: Hypothesis={Hypothesis}, Confidence={Confidence}",
                symptomData.Hypothesis, symptomData.Confidence);

            // Step 3: Create assessment (deterministic - always executes if intent is create_assessment)
            var createResult = await assessmentTools.CreateAssessmentAsync(
                symptomData.Hypothesis,
                symptomData.Confidence,
                symptomData.Differentials,
                symptomData.Reasoning ?? string.Empty,
                symptomData.RecommendedAction);

            if (createResult.CreatedAssessment == null)
            {
                logger.LogError("Failed to create assessment: {Error}", createResult.ErrorMessage);
                var errorResponse = $"I encountered an error creating your assessment: {createResult.ErrorMessage}";
                state[WorkflowStateKeys.Response] = errorResponse;
                return new WorkflowResult { Success = false, State = state, Response = errorResponse };
            }

            state[WorkflowStateKeys.AssessmentId] = createResult.CreatedAssessment.Id;
            logger.LogInformation("Created assessment {AssessmentId}", createResult.CreatedAssessment.Id);

            // Step 4: Complete assessment (deterministic - always follows create)
            var completeResult = await assessmentTools.CompleteAssessmentAsync(createResult.CreatedAssessment.Id);
            if (completeResult.CompletedAssessmentId == null)
            {
                logger.LogWarning("CompleteAssessment returned null, but continuing");
            }
            else
            {
                logger.LogInformation("Completed assessment {AssessmentId}", completeResult.CompletedAssessmentId);
            }

            // Step 5: Generate final response
            var finalResponse = await GenerateFinalResponseAsync(state, context);
            state[WorkflowStateKeys.Response] = finalResponse;

            return new WorkflowResult
            {
                Success = true,
                State = state,
                Response = finalResponse,
                AssessmentId = createResult.CreatedAssessment.Id
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing assessment workflow");
            var errorResponse = "I encountered an error processing your request. Please try again.";
            state[WorkflowStateKeys.Response] = errorResponse;
            return new WorkflowResult { Success = false, State = state, Response = errorResponse };
        }
    }

    /// <summary>
    /// Classifies user intent - determines if assessment is requested.
    /// </summary>
    private async Task<string> ClassifyIntentAsync(string userMessage)
    {
        var assessmentKeywords = new[] { "assessment", "assess", "diagnosis", "evaluate", "evaluation", "generate assessment", "create assessment" };
        var userMessageLower = userMessage.ToLowerInvariant();

        if (assessmentKeywords.Any(keyword => userMessageLower.Contains(keyword)))
        {
            return "create_assessment";
        }

        return "other";
    }

    /// <summary>
    /// Extracts symptom data from user message using LLM with structured output.
    /// </summary>
    private async Task<SymptomData> ExtractSymptomsWithLLMAsync(string userMessage, ConversationContext context)
    {
        try
        {
            // Get active episodes for context
            var activeEpisodes = symptomTrackerTools.GetActiveEpisodes();
            var episodeNames = activeEpisodes.ActiveEpisodes
                .Where(e => e.Symptom != null)
                .Select(e => e.Symptom!.Name)
                .Distinct()
                .ToList();

            var systemPrompt = @"You are a healthcare assistant. Extract symptom information from the user's message and return JSON with this structure:
{
    ""hypothesis"": ""primary diagnosis (e.g. 'viral infection', 'influenza', 'migraine')"",
    ""confidence"": 0.7,
    ""differentials"": [""alternative diagnoses""],
    ""reasoning"": ""brief explanation"",
    ""recommendedAction"": ""see-gp""
}

Recommended actions: 'see-gp', 'urgent-care', 'emergency', or 'self-care'.
Use confidence 0.7 if unsure, 0.8-0.9 if confident.";

            var contextInfo = episodeNames.Any()
                ? $"\n\nCurrent active symptoms: {string.Join(", ", episodeNames)}"
                : "";

            // Create agent with structured output support
            var agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
            {
                ChatOptions = new()
                {
                    Instructions = systemPrompt + contextInfo,
                    ResponseFormat = ChatResponseFormat.ForJsonSchema<SymptomData>()
                }
            });

            // Run agent with structured output
            var response = await agent.RunAsync<SymptomData>(userMessage);

            if (response.Result == null)
            {
                logger.LogWarning("Failed to extract symptom data, using defaults");
                return new SymptomData
                {
                    Hypothesis = "General health concern",
                    Confidence = 0.7m,
                    RecommendedAction = "see-gp"
                };
            }

            logger.LogDebug("LLM extracted symptom data: Hypothesis={Hypothesis}, Confidence={Confidence}",
                response.Result.Hypothesis, response.Result.Confidence);

            return response.Result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error extracting symptoms with LLM, using defaults");
            return new SymptomData
            {
                Hypothesis = "General health concern",
                Confidence = 0.7m,
                RecommendedAction = "see-gp"
            };
        }
    }

    /// <summary>
    /// Generates final natural language response.
    /// </summary>
    private async Task<string> GenerateFinalResponseAsync(Dictionary<string, object> state, ConversationContext context)
    {
        try
        {
            var userMessage = (string)state[WorkflowStateKeys.UserMessage];
            var intent = (string)state[WorkflowStateKeys.Intent];

            var systemPrompt = @"You are a helpful healthcare assistant. Provide a natural, empathetic response to the user.
If an assessment was created, explain what you found and what the user should do next.
Be concise and clear.";

            // Add context about what happened
            if (state.ContainsKey(WorkflowStateKeys.AssessmentId))
            {
                var assessmentId = (int)state[WorkflowStateKeys.AssessmentId];
                if (state.ContainsKey(WorkflowStateKeys.Symptoms))
                {
                    var symptomData = (SymptomData)state[WorkflowStateKeys.Symptoms];
                    systemPrompt += $"\n\nAn assessment was created (ID: {assessmentId}) with hypothesis: {symptomData.Hypothesis}, " +
                        $"confidence: {symptomData.Confidence:P0}, recommended action: {symptomData.RecommendedAction}";
                }
            }

            // Create agent
            var agent = new ChatClientAgent(chatClient, systemPrompt);

            // Build messages
            var messages = new List<ChatMessage>
            {
                new(ChatRole.User, userMessage)
            };

            // Run agent
            var response = await agent.RunAsync(messages);
            var content = response.Text ?? "";

            return content;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error generating final response");
            return "I've processed your request and created an assessment. Is there anything else you'd like to discuss?";
        }
    }
}

/// <summary>
/// Result from workflow execution.
/// </summary>
public class WorkflowResult
{
    public bool Success { get; set; }
    public Dictionary<string, object> State { get; set; } = new();
    public string Response { get; set; } = string.Empty;
    public int? AssessmentId { get; set; }
}
