using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Web.Common.DTOs.Health;
using WebApi.Controllers;
using WebApi.Data;
using Web.Common.DTOs.Conversations;
using Web.Common.DTOs.AI;
using WebApi.Exceptions;
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
    ILogger<HealthChatOrchestrator> logger)
{
    public async Task<(HealthChatResponse Response, bool IsNewConversation)> ProcessHealthMessageAsync(
        Guid userId,
        string message,
        Guid? conversationId = null)
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

        var healthResponse = await ProcessMessageAsync(
            userId,
            message,
            conversation.Id);

        var routedResponse = responseRouter.RouteResponse(healthResponse, userId);

        // Track changes by comparing before/after state
        var symptomChanges = await TrackEpisodeChangesAsync(userId, routedResponse.SymptomChanges, episodesBeforeDict);
        var appointmentChanges = await TrackAppointmentChangesAsync(userId, appointmentsBefore);

        // Convert EntityChanges to status information JSON
        var statusInformationJson = SerializeStatusInformation(symptomChanges, appointmentChanges);

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
            AppointmentChanges = appointmentChanges
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

    private static string? SerializeStatusInformation(List<EntityChange> symptomChanges, List<EntityChange> appointmentChanges)
    {
        if (!symptomChanges.Any() && !appointmentChanges.Any())
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

    private async Task<HealthAssistantResponse> ProcessMessageAsync(
        Guid userId,
        string userMessage,
        Guid? conversationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new HealthChatScenarioRequest
            {
                Message = userMessage,
                ConversationId = conversationId,
                UserId = userId
            };

            var response = await scenario.ExecuteAsync(request, cancellationToken);

            // Parse JSON response
            return ParseHealthResponse(response.Message);
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

            if (healthResponse != null)
            {
                return healthResponse;
            }
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse JSON response, using fallback");
        }

        // Fallback to plain text response
        return new HealthAssistantResponse
        {
            Message = responseText,
            Appointment = null,
            SymptomChanges = null
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
        return json.Trim();
    }
}
