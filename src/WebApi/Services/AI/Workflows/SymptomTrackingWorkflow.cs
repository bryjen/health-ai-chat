using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using WebApi.Hubs;
using WebApi.Models;
using WebApi.Services.AI.Tools;
using WebApi.Services.Chat.Conversations;

namespace WebApi.Services.AI.Workflows;

/// <summary>
/// Workflow for symptom tracking operations.
/// Handles symptom detection, episode creation, and updates.
/// </summary>
public class SymptomTrackingWorkflow(
    SymptomTrackerTools symptomTrackerTools,
    ConversationContextService contextService,
    IChatClient chatClient,
    ILogger<SymptomTrackingWorkflow> logger)
{
    private ClientConnection? _clientConnection;

    public void SetConnection(ClientConnection? clientConnection)
    {
        _clientConnection = clientConnection;
        symptomTrackerTools.SetConnection(clientConnection);
    }

    /// <summary>
    /// Executes the symptom tracking workflow.
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
            // Step 1: Detect symptoms in user message
            var detectedSymptoms = await DetectSymptomsAsync(userMessage, context);
            state["detectedSymptoms"] = detectedSymptoms;
            logger.LogInformation("Detected {Count} symptoms", detectedSymptoms.Count);

            // Step 2: Create episodes for new symptoms
            var createdEpisodes = new List<Episode>();
            foreach (var symptomName in detectedSymptoms)
            {
                var result = await symptomTrackerTools.CreateSymptomWithEpisodeAsync(symptomName);
                if (result.CreatedEpisode != null)
                {
                    createdEpisodes.Add(result.CreatedEpisode);
                    logger.LogInformation("Created episode {EpisodeId} for symptom {SymptomName}",
                        result.CreatedEpisode.Id, symptomName);
                }
                else if (result.Episode != null)
                {
                    // Existing episode found
                    createdEpisodes.Add(result.Episode);
                }
            }

            state["createdEpisodes"] = createdEpisodes;

            // Step 3: Update episodes with additional details if mentioned
            // This could be enhanced to extract severity, location, etc. from the message
            // For now, we'll let the LLM response handle asking for more details

            // Step 4: Generate response
            var response = await GenerateResponseAsync(userMessage, context, createdEpisodes);
            state[WorkflowStateKeys.Response] = response;

            return new WorkflowResult
            {
                Success = true,
                State = state,
                Response = response
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error executing symptom tracking workflow");
            var errorResponse = "I encountered an error processing your symptoms. Please try again.";
            state[WorkflowStateKeys.Response] = errorResponse;
            return new WorkflowResult { Success = false, State = state, Response = errorResponse };
        }
    }

    /// <summary>
    /// Detects symptom names mentioned in the user message.
    /// </summary>
    private async Task<List<string>> DetectSymptomsAsync(string userMessage, ConversationContext context)
    {
        try
        {
            // Get active episodes for context
            var activeEpisodes = symptomTrackerTools.GetActiveEpisodes();
            var activeSymptomNames = activeEpisodes.ActiveEpisodes
                .Where(e => e.Symptom != null)
                .Select(e => e.Symptom!.Name)
                .Distinct()
                .ToList();

            var systemPrompt = @"You are a healthcare assistant. Extract symptom names from the user's message.
Return a JSON object with a 'symptoms' array containing the symptom names mentioned.
Example: {""symptoms"": [""headache"", ""fever""]}
If no symptoms are found, return {""symptoms"": []}.";

            var contextInfo = activeSymptomNames.Any()
                ? $"\n\nCurrent active symptoms: {string.Join(", ", activeSymptomNames)}"
                : "";

            // Create agent with structured output support
            var agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
            {
                ChatOptions = new()
                {
                    Instructions = systemPrompt + contextInfo,
                    ResponseFormat = ChatResponseFormat.ForJsonSchema<DetectedSymptoms>()
                }
            });

            // Run agent with structured output
            var response = await agent.RunAsync<DetectedSymptoms>(userMessage);

            if (response.Result == null)
            {
                logger.LogWarning("Failed to extract symptoms, using fallback");
                // Fallback: simple keyword detection
                var commonSymptoms = new[] { "headache", "fever", "cough", "pain", "nausea", "dizziness" };
                return commonSymptoms.Where(s => userMessage.ToLowerInvariant().Contains(s)).ToList();
            }

            var symptoms = response.Result.Symptoms ?? new List<string>();
            logger.LogDebug("LLM detected {Count} symptoms: {Symptoms}", symptoms.Count, string.Join(", ", symptoms));

            return symptoms;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error detecting symptoms with LLM");
            // Fallback: simple keyword detection
            var commonSymptoms = new[] { "headache", "fever", "cough", "pain", "nausea", "dizziness" };
            return commonSymptoms.Where(s => userMessage.ToLowerInvariant().Contains(s)).ToList();
        }
    }

    /// <summary>
    /// Generates a natural language response about symptom tracking.
    /// </summary>
    private async Task<string> GenerateResponseAsync(
        string userMessage,
        ConversationContext context,
        List<Episode> createdEpisodes)
    {
        try
        {
            var systemPrompt = @"You are a helpful healthcare assistant. The user has reported symptoms.
Acknowledge the symptoms you've tracked and ask follow-up questions if needed (severity, location, frequency, triggers, relievers, pattern).
Be empathetic and concise.";

            var episodeInfo = createdEpisodes.Any()
                ? $"\n\nTracked symptoms: {string.Join(", ", createdEpisodes.Select(e => e.Symptom?.Name ?? "Unknown"))}"
                : "";

            // Create agent
            var agent = new ChatClientAgent(chatClient, systemPrompt + episodeInfo);

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
            logger.LogError(ex, "Error generating response");
            var symptomNames = string.Join(", ", createdEpisodes.Select(e => e.Symptom?.Name ?? "symptoms"));
            return $"I've tracked your {symptomNames}. Is there anything else you'd like to tell me about these symptoms?";
        }
    }
}
