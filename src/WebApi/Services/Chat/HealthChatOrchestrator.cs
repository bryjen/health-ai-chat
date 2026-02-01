using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Web.Common.DTOs.Health;
using WebApi.Controllers;
using WebApi.Data;
using Web.Common.DTOs.Conversations;
using Web.Common.DTOs.AI;
using WebApi.Exceptions;
using WebApi.Hubs;
using WebApi.Models;
using WebApi.Services.AI.Scenarios;
using WebApi.Services.VectorStore;

namespace WebApi.Services.Chat;

/// <summary>
/// Orchestrates the health chat flow: conversation management, AI processing, message persistence, and embedding storage.
/// </summary>
public class HealthChatOrchestrator(
    HealthChatScenario scenario,
    ResponseRouterService responseRouter,
    VectorStoreService vectorStoreService,
    AppDbContext context,
    IStatusUpdateService statusUpdateService,
    ILogger<HealthChatOrchestrator> logger)
{
    public async Task<(HealthChatResponse Response, bool IsNewConversation)> ProcessHealthMessageAsync(
        Guid userId,
        string message,
        Guid? conversationId = null,
        string? connectionId = null)
    {
        var (conversation, isNewConversation) = await GetOrCreateConversationAsync(userId, message, conversationId);

        // Track state before processing to detect changes
        var episodesBefore = await context.Episodes
            .Where(e => e.UserId == userId && e.Status == "active")
            .Select(e => new { e.Id, SymptomName = e.Symptom != null ? e.Symptom.Name : "Unknown" })
            .ToListAsync();
        var episodesBeforeDict = episodesBefore.ToDictionary(e => e.Id, e => e.SymptomName);
        var appointmentsBefore = await context.Appointments
            .Where(a => a.UserId == userId)
            .Select(a => a.Id)
            .ToListAsync();
        var assessmentsBefore = await context.Assessments
            .Where(a => a.UserId == userId && a.ConversationId == conversation.Id)
            .Select(a => a.Id)
            .ToListAsync();

        var healthResponse = await ProcessMessageAsync(
            userId,
            message,
            conversation.Id,
            connectionId);

        var routedResponse = responseRouter.RouteResponse(healthResponse, userId);

        // Track changes by comparing before/after state
        var symptomChanges = await TrackEpisodeChangesAsync(userId, routedResponse.SymptomChanges, episodesBeforeDict);
        var appointmentChanges = await TrackAppointmentChangesAsync(userId, appointmentsBefore);
        var assessmentChanges = await TrackAssessmentChangesAsync(userId, conversation.Id, assessmentsBefore);

        // Merge real-time status updates with EntityChanges-based statuses
        var statusInformationJson = SerializeStatusInformation(
            symptomChanges, 
            appointmentChanges, 
            assessmentChanges,
            routedResponse.StatusUpdatesSent);

        var (userMessage, assistantMessage) = await SaveMessagesAsync(
            conversation.Id,
            message,
            routedResponse.Message,
            statusInformationJson);

        await vectorStoreService.StoreMessageAsync(userId, userMessage.Id, message);
        await vectorStoreService.StoreMessageAsync(userId, assistantMessage.Id, routedResponse.Message);

        conversation.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        var response = new HealthChatResponse
        {
            Message = routedResponse.Message,
            ConversationId = conversation.Id,
            SymptomChanges = symptomChanges,
            AppointmentChanges = appointmentChanges,
            AssessmentChanges = assessmentChanges
        };

        return (response, isNewConversation);
    }

    private async Task<(Conversation Conversation, bool IsNewConversation)> GetOrCreateConversationAsync(
        Guid userId,
        string message,
        Guid? conversationId)
    {
        if (conversationId.HasValue)
        {
            // Continue existing conversation
            logger.LogDebug("Looking up existing conversation {ConversationId} for user {UserId}", conversationId.Value, userId);
            var conversation = await context.Conversations
                .FirstOrDefaultAsync(c => c.Id == conversationId.Value && c.UserId == userId);

            if (conversation == null)
            {
                logger.LogWarning("Conversation {ConversationId} not found for user {UserId}", conversationId.Value, userId);
                throw new NotFoundException("Conversation not found");
            }

            logger.LogDebug("Found existing conversation {ConversationId}, continuing conversation", conversation.Id);
            return (conversation, false);
        }
        else
        {
            // Create new conversation
            logger.LogDebug("No conversationId provided, creating new conversation for user {UserId}", userId);
            var title = message.Length > 50
                ? message.Substring(0, 50) + "..."
                : message;

            var conversation = new Conversation
            {
                Id = Guid.NewGuid(),
                Title = title,
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            context.Conversations.Add(conversation);
            await context.SaveChangesAsync(); // Save to get the ID

            logger.LogDebug("Created new conversation {ConversationId} for user {UserId}", conversation.Id, userId);
            return (conversation, true);
        }
    }

    private async Task<(Message UserMessage, Message AssistantMessage)> SaveMessagesAsync(
        Guid conversationId,
        string userMessageContent,
        string assistantMessageContent,
        string? statusInformationJson = null)
    {
        var userMessage = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = "user",
            Content = userMessageContent,
            CreatedAt = DateTime.UtcNow
        };

        var assistantMessage = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            Role = "assistant",
            Content = assistantMessageContent,
            StatusInformationJson = statusInformationJson,
            CreatedAt = DateTime.UtcNow
        };

        context.Messages.Add(userMessage);
        context.Messages.Add(assistantMessage);
        await context.SaveChangesAsync();

        return (userMessage, assistantMessage);
    }

    private static string? SerializeStatusInformation(
        List<EntityChange> symptomChanges, 
        List<EntityChange> appointmentChanges,
        List<EntityChange> assessmentChanges,
        List<object>? realTimeStatusUpdates = null)
    {
        // Include real-time status updates even if no EntityChanges
        if (!symptomChanges.Any() && !appointmentChanges.Any() && !assessmentChanges.Any() && 
            (realTimeStatusUpdates == null || !realTimeStatusUpdates.Any()))
        {
            return null;
        }

        var statusList = new List<object>();

        foreach (var change in symptomChanges)
        {
            switch (change.Action.ToLowerInvariant())
            {
                case "created":
                    if (int.TryParse(change.Id, out var episodeId))
                    {
                        statusList.Add(new
                        {
                            type = "symptom-added",
                            symptomName = change.Name ?? "Unknown symptom",
                            episodeId = episodeId,
                            location = (string?)null,
                            timestamp = DateTime.UtcNow
                        });
                    }
                    break;

                case "updated":
                    statusList.Add(new
                    {
                        type = "general",
                        message = !string.IsNullOrEmpty(change.Name)
                            ? $"Updated {change.Name} details"
                            : "Updated symptom details",
                        timestamp = DateTime.UtcNow
                    });
                    break;

                case "resolved":
                    statusList.Add(new
                    {
                        type = "general",
                        message = !string.IsNullOrEmpty(change.Name)
                            ? $"Resolved {change.Name}"
                            : "Resolved symptom",
                        timestamp = DateTime.UtcNow
                    });
                    break;
            }
        }

        // Add assessment changes
        foreach (var change in assessmentChanges)
        {
            switch (change.Action.ToLowerInvariant())
            {
                case "created":
                    if (int.TryParse(change.Id, out var assessmentId))
                    {
                        // Check if we already have this from real-time updates
                        var alreadyExists = realTimeStatusUpdates?.Any(s => 
                            System.Text.Json.JsonSerializer.Serialize(s).Contains($"\"assessmentId\":{assessmentId}")) == true;
                        
                        if (!alreadyExists)
                        {
                            statusList.Add(new
                            {
                                type = "assessment-created",
                                assessmentId = assessmentId,
                                hypothesis = change.Name ?? "Assessment",
                                confidence = change.Confidence ?? 0m,
                                timestamp = DateTime.UtcNow
                            });
                        }
                    }
                    break;
            }
        }

        // Add real-time status updates (assessment-generating, assessment-complete, assessment-analyzing, assessment-created)
        // These are sent during processing and should be persisted
        // Sort them by type order first, then timestamp to maintain correct order
        if (realTimeStatusUpdates != null && realTimeStatusUpdates.Any())
        {
            // Sort real-time updates by type order (generating -> created -> analyzing -> complete) then timestamp
            var sortedUpdates = realTimeStatusUpdates
                .Select(update =>
                {
                    var json = JsonSerializer.Serialize(update);
                    using var doc = JsonDocument.Parse(json);
                    var timestamp = doc.RootElement.TryGetProperty("timestamp", out var ts) 
                        ? ts.GetDateTime() 
                        : DateTime.UtcNow;
                    var type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : "";
                    var typeOrder = type switch
                    {
                        "assessment-generating" => 1,
                        "assessment-created" => 2,
                        "assessment-analyzing" => 3,
                        "assessment-complete" => 4, // Keep for backwards compatibility
                        _ => 5
                    };
                    return new { Update = update, Timestamp = timestamp, TypeOrder = typeOrder };
                })
                .OrderBy(x => x.TypeOrder)
                .ThenBy(x => x.Timestamp)
                .Select(x => x.Update)
                .ToList();

            foreach (var statusUpdate in sortedUpdates)
            {
                // Deserialize to check type and avoid duplicates
                var statusJson = JsonSerializer.Serialize(statusUpdate);
                using var doc = JsonDocument.Parse(statusJson);
                
                if (doc.RootElement.TryGetProperty("type", out var typeElement))
                {
                    var type = typeElement.GetString();
                    
                    // Only add if not already present (avoid duplicates with EntityChanges)
                    if (type == "assessment-created")
                    {
                        // Always add assessment-created from real-time updates (it has the link)
                        // Check if we already added this assessment from EntityChanges
                        if (doc.RootElement.TryGetProperty("assessmentId", out var idElement))
                        {
                            var id = idElement.GetInt32();
                            var alreadyExists = statusList.Any(s => 
                                System.Text.Json.JsonSerializer.Serialize(s).Contains($"\"assessmentId\":{id}"));
                            if (!alreadyExists)
                            {
                                statusList.Add(statusUpdate);
                            }
                        }
                        else
                        {
                            // Add even without ID if it's from real-time
                            statusList.Add(statusUpdate);
                        }
                    }
                    else
                    {
                        // For other types (generating, complete, analyzing), always add
                        // Check if we already have this exact status to avoid duplicates
                        var alreadyExists = statusList.Any(s =>
                        {
                            var sJson = JsonSerializer.Serialize(s);
                            using var sDoc = JsonDocument.Parse(sJson);
                            if (sDoc.RootElement.TryGetProperty("type", out var sType))
                            {
                                return sType.GetString() == type;
                            }
                            return false;
                        });
                        
                        if (!alreadyExists)
                        {
                            statusList.Add(statusUpdate);
                        }
                    }
                }
            }
        }

        if (!statusList.Any())
        {
            return null;
        }

        return JsonSerializer.Serialize(statusList);
    }

    private async Task<List<EntityChange>> TrackEpisodeChangesAsync(
        Guid userId,
        List<SymptomChange>? symptomChangesFromAi,
        Dictionary<int, string> episodesBeforeDict)
    {
        var changes = new List<EntityChange>();

        // Find episodes created or updated in the last 30 seconds (should cover the AI processing time)
        var recentCutoff = DateTime.UtcNow.AddSeconds(-30);
        var recentEpisodes = await context.Episodes
            .Where(e => e.UserId == userId && 
                       (e.CreatedAt >= recentCutoff || e.UpdatedAt >= recentCutoff))
            .Include(e => e.Symptom)
            .ToListAsync();

        foreach (var episode in recentEpisodes)
        {
            var symptomName = episode.Symptom?.Name ?? "Unknown";
            var wasCreated = episode.CreatedAt >= recentCutoff;
            var wasUpdated = !wasCreated && episode.UpdatedAt >= recentCutoff;

            if (wasCreated && !episodesBeforeDict.ContainsKey(episode.Id))
            {
                changes.Add(new EntityChange
                {
                    Id = episode.Id.ToString(),
                    Action = "created",
                    Name = symptomName
                });
            }
            else if (wasUpdated && episodesBeforeDict.ContainsKey(episode.Id))
            {
                changes.Add(new EntityChange
                {
                    Id = episode.Id.ToString(),
                    Action = "updated",
                    Name = symptomName
                });
            }
        }

        // Also check for resolved episodes
        var resolvedEpisodes = await context.Episodes
            .Where(e => e.UserId == userId && 
                       e.Status == "resolved" && 
                       e.ResolvedAt >= recentCutoff)
            .Include(e => e.Symptom)
            .ToListAsync();

        foreach (var episode in resolvedEpisodes)
        {
            if (episodesBeforeDict.ContainsKey(episode.Id))
            {
                var resolvedSymptomName = episode.Symptom?.Name ?? "Unknown";
                changes.Add(new EntityChange
                {
                    Id = episode.Id.ToString(),
                    Action = "resolved",
                    Name = resolvedSymptomName
                });
            }
        }

        return changes;
    }

    private async Task<List<EntityChange>> TrackAppointmentChangesAsync(
        Guid userId,
        List<Guid> appointmentsBefore)
    {
        var changes = new List<EntityChange>();

        // Find appointments created or updated in the last 30 seconds (should cover the AI processing time)
        var recentCutoff = DateTime.UtcNow.AddSeconds(-30);
        var recentAppointments = await context.Appointments
            .Where(a => a.UserId == userId && 
                       (a.CreatedAt >= recentCutoff || a.UpdatedAt >= recentCutoff))
            .ToListAsync();

        foreach (var appointment in recentAppointments)
        {
            var wasCreated = appointment.CreatedAt >= recentCutoff;
            var wasUpdated = !wasCreated && appointment.UpdatedAt >= recentCutoff;

            if (wasCreated && !appointmentsBefore.Contains(appointment.Id))
            {
                changes.Add(new EntityChange
                {
                    Id = appointment.Id.ToString(),
                    Action = "created"
                });
            }
            else if (wasUpdated && appointmentsBefore.Contains(appointment.Id))
            {
                changes.Add(new EntityChange
                {
                    Id = appointment.Id.ToString(),
                    Action = "updated"
                });
            }
        }

        return changes;
    }

    private async Task<List<EntityChange>> TrackAssessmentChangesAsync(
        Guid userId,
        Guid conversationId,
        List<int> assessmentsBefore)
    {
        var changes = new List<EntityChange>();

        // Find assessments created in the last 30 seconds
        var recentCutoff = DateTime.UtcNow.AddSeconds(-30);
        var recentAssessments = await context.Assessments
            .Where(a => a.UserId == userId && 
                       a.ConversationId == conversationId &&
                       a.CreatedAt >= recentCutoff)
            .ToListAsync();

        foreach (var assessment in recentAssessments)
        {
            if (!assessmentsBefore.Contains(assessment.Id))
            {
                changes.Add(new EntityChange
                {
                    Id = assessment.Id.ToString(),
                    Action = "created",
                    Name = assessment.Hypothesis,
                    Confidence = assessment.Confidence
                });
            }
        }

        return changes;
    }

    private async Task<HealthAssistantResponse> ProcessMessageAsync(
        Guid userId,
        string userMessage,
        Guid? conversationId,
        string? connectionId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new HealthChatScenarioRequest
            {
                Message = userMessage,
                ConversationId = conversationId,
                UserId = userId,
                ConnectionId = connectionId
            };

            HealthChatScenarioResponse response;
            List<object> statusUpdatesSent = new();
            if (scenario is HealthChatScenario healthChatScenario)
            {
                response = await healthChatScenario.ExecuteAsyncInternal(request, cancellationToken, statusUpdateService);
                statusUpdatesSent = response.StatusUpdatesSent ?? new List<object>();
            }
            else
            {
                response = await scenario.ExecuteAsync(request, cancellationToken);
            }

            // Parse JSON response
            var parsedResponse = ParseHealthResponse(response.Message);
            
            // Store status updates for later persistence
            parsedResponse.StatusUpdatesSent = statusUpdatesSent;
            
            return parsedResponse;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing health chat message for user {UserId}", userId);
            throw;
        }
    }

    private HealthAssistantResponse ParseHealthResponse(string responseText)
    {
        try
        {
            // Try to extract JSON from response (might be wrapped in markdown code blocks)
            var jsonText = ExtractJsonFromResponse(responseText);
            var healthResponse = JsonSerializer.Deserialize<HealthAssistantResponse>(jsonText, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (healthResponse != null && !string.IsNullOrWhiteSpace(healthResponse.Message))
            {
                // Successfully parsed JSON - preserve status updates if they exist
                healthResponse.StatusUpdatesSent = healthResponse.StatusUpdatesSent ?? new List<object>();
                return healthResponse;
            }
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse JSON response, attempting to extract message text");
        }

        // If JSON parsing failed, try to extract just the message text
        // Look for "message" field value in the response
        try
        {
            var jsonText = ExtractJsonFromResponse(responseText);
            using var doc = JsonDocument.Parse(jsonText);
            
            if (doc.RootElement.TryGetProperty("message", out var messageElement))
            {
                var messageText = messageElement.GetString();
                if (!string.IsNullOrWhiteSpace(messageText))
                {
                    return new HealthAssistantResponse
                    {
                        Message = messageText,
                        Appointment = null,
                        SymptomChanges = null,
                        StatusUpdatesSent = new List<object>()
                    };
                }
            }
        }
        catch
        {
            // If that fails, continue to fallback
        }

        // Final fallback: if response contains JSON-like structure, try to extract message field
        // Otherwise return the response as-is but clean it up
        var cleanedResponse = responseText.Trim();
        
        // Remove any trailing JSON if message text appears before it
        var messageMatch = System.Text.RegularExpressions.Regex.Match(
            cleanedResponse, 
            @"""message""\s*:\s*""([^""]+)""",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        if (messageMatch.Success && messageMatch.Groups.Count > 1)
        {
            var extractedMessage = messageMatch.Groups[1].Value;
            // Unescape JSON string
            extractedMessage = extractedMessage.Replace("\\n", "\n").Replace("\\\"", "\"");
            return new HealthAssistantResponse
            {
                Message = extractedMessage,
                Appointment = null,
                SymptomChanges = null
            };
        }

        // Last resort: return cleaned response (but log warning)
        logger.LogWarning("Could not parse JSON response, returning raw text. Response length: {Length}", responseText.Length);
        return new HealthAssistantResponse
        {
            Message = cleanedResponse,
            Appointment = null,
            SymptomChanges = null,
            StatusUpdatesSent = new List<object>()
        };
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
        // Look for first { and last } to extract just the JSON object
        var firstBrace = json.IndexOf('{');
        var lastBrace = json.LastIndexOf('}');
        
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            // Extract just the JSON object part
            var jsonObject = json.Substring(firstBrace, lastBrace - firstBrace + 1);
            
            // Verify it's valid JSON by trying to parse it
            try
            {
                using var doc = JsonDocument.Parse(jsonObject);
                return jsonObject;
            }
            catch
            {
                // If extraction fails, return original
            }
        }

        return json;
    }
}
