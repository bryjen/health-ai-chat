using System.ComponentModel;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using WebApi.Hubs;
using WebApi.Models;
using WebApi.Repositories;
using WebApi.Services.Chat;
using WebApi.Services.Chat.Conversations;

namespace WebApi.Services.AI.Plugins;

/// <summary>
/// Semantic Kernel plugin for symptom tracking operations.
/// Provides kernel functions that the AI can call to create and manage symptom episodes.
/// </summary>
public class SymptomTrackerPlugin(
    SymptomRepository symptomRepository,
    EpisodeRepository episodeRepository,
    NegativeFindingRepository negativeFindingRepository,
    ConversationContextService contextService,
    ILogger<SymptomTrackerPlugin> logger)
{
    private ClientConnection? _clientConnection;

    public void SetConnection(ClientConnection? clientConnection)
    {
        _clientConnection = clientConnection;
    }

    private ConversationContext GetContext()
    {
        return contextService.GetCurrentContext();
    }

    private ClientConnection? GetClientConnection()
    {
        return _clientConnection;
    }

    [KernelFunction]
    [Description("CreateSymptomWithEpisode: YOU MUST CALL THIS FUNCTION immediately when a user reports ANY symptom. This function saves the symptom to the database. DO NOT just acknowledge symptoms in text - you MUST call this function to track them. Parameters: name (required - the symptom name like 'headache', 'fever', 'cough'), description (optional additional details). Returns the created episode ID which you can use with UpdateEpisode.")]
    public async Task<string> CreateSymptomWithEpisodeAsync(
        [Description("The name of the symptom")] string name,
        [Description("Optional description of the symptom")] string? description = null)
    {
        var context = GetContext();
        var clientConnection = GetClientConnection();

        try
        {
            // Get or create symptom type
            var symptom = await symptomRepository.GetOrCreateSymptomAsync(context.UserId, name, description);

            // Check if there's a recent episode for this symptom
            if (context.RecentEpisodesBySymptom.TryGetValue(name, out var recentEpisode))
            {
                logger.LogInformation("Found recent episode {EpisodeId} for symptom {SymptomName}", recentEpisode.Id, name);
                return $"Found existing episode {recentEpisode.Id} for {name}. Use UpdateEpisode to add details.";
            }

            // Create new episode
            var episode = await episodeRepository.CreateEpisodeAsync(
                context.UserId,
                symptom.Id,
                DateTime.UtcNow,
                stage: "mentioned",
                status: "active");

            // Update context
            context.ActiveEpisodes.Add(episode);
            context.ActiveSymptoms.Add(symptom);
            context.RecentEpisodesBySymptom[name] = episode;

            // Send status update (fire-and-forget)
            clientConnection?.SendSymptomAdded(episode.Id, name, null);

            logger.LogInformation("Created episode {EpisodeId} for symptom {SymptomName}", episode.Id, name);
            return $"Created episode {episode.Id} for {name}.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating symptom with episode for {SymptomName}", name);
            return $"Error creating episode: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("UpdateEpisode: YOU MUST CALL THIS FUNCTION to add details to an episode (severity, location, frequency, triggers, relievers, pattern). Call this every time you learn new information about a symptom episode. Parameters: episodeId (required), then any details you learned (severity 1-10, location, frequency constant/intermittent/occasional, triggers list, relievers list, pattern description).")]
    public async Task<string> UpdateEpisodeAsync(
        [Description("The ID of the episode to update")] int episodeId,
        [Description("Severity on a scale of 1-10")] int? severity = null,
        [Description("Location where the symptom occurs")] string? location = null,
        [Description("Frequency: constant, intermittent, or occasional")] string? frequency = null,
        [Description("List of triggers")] List<string>? triggers = null,
        [Description("List of relievers")] List<string>? relievers = null,
        [Description("Pattern description (e.g., 'worse in morning')")] string? pattern = null)
    {
        var context = GetContext();
        var clientConnection = GetClientConnection();

        try
        {
            var episode = await episodeRepository.UpdateEpisodeAsync(
                episodeId,
                severity,
                location,
                frequency,
                triggers,
                relievers,
                pattern);

            if (episode == null)
            {
                return $"Episode {episodeId} not found.";
            }

            // Update context
            var contextEpisode = context.ActiveEpisodes.FirstOrDefault(e => e.Id == episodeId);
            if (contextEpisode != null)
            {
                contextEpisode.Severity = episode.Severity;
                contextEpisode.Location = episode.Location;
                contextEpisode.Frequency = episode.Frequency;
                contextEpisode.Triggers = episode.Triggers;
                contextEpisode.Relievers = episode.Relievers;
                contextEpisode.Pattern = episode.Pattern;
                contextEpisode.Stage = episode.Stage;
            }

            // Send status update (fire-and-forget)
            var symptomName = episode.Symptom?.Name ?? "Unknown symptom";
            clientConnection?.SendSymptomUpdated(episodeId, symptomName);

            logger.LogInformation("Updated episode {EpisodeId}, stage: {Stage}", episodeId, episode.Stage);
            return $"Updated episode {episodeId}. Stage: {episode.Stage}.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating episode {EpisodeId}", episodeId);
            return $"Error updating episode: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("LinkEpisodeToExisting: Links an episode to another related episode, marking it as 'linked' stage.")]
    public async Task<string> LinkEpisodeToExistingAsync(
        [Description("The ID of the episode to link")] int episodeId,
        [Description("The ID of the related episode")] int relatedEpisodeId)
    {
        var context = GetContext();
        var clientConnection = GetClientConnection();

        try
        {
            clientConnection?.SendProcessing("Linking episodes");

            var success = await episodeRepository.LinkEpisodesAsync(episodeId, relatedEpisodeId);
            if (!success)
            {
                return $"Episode {episodeId} not found.";
            }

            // Update context
            var episode = context.ActiveEpisodes.FirstOrDefault(e => e.Id == episodeId);
            var symptomName = episode?.Symptom?.Name ?? "Unknown symptom";
            if (episode != null)
            {
                episode.Stage = "linked";
            }

            clientConnection?.SendCompleted($"Linked {symptomName} episode");
            logger.LogInformation("Linked episode {EpisodeId} to {RelatedEpisodeId}", episodeId, relatedEpisodeId);
            return $"Linked episode {episodeId} to episode {relatedEpisodeId}.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error linking episodes {EpisodeId} to {RelatedEpisodeId}", episodeId, relatedEpisodeId);
            return $"Error linking episodes: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("ResolveEpisode: Marks an episode as resolved, setting its status to 'resolved' and recording the resolution date.")]
    public async Task<string> ResolveEpisodeAsync(
        [Description("The ID of the episode to resolve")] int episodeId)
    {
        var context = GetContext();
        var clientConnection = GetClientConnection();

        try
        {
            var success = await episodeRepository.ResolveEpisodeAsync(episodeId);
            if (!success)
            {
                return $"Episode {episodeId} not found.";
            }

            // Update context
            var episode = context.ActiveEpisodes.FirstOrDefault(e => e.Id == episodeId);
            var symptomName = episode?.Symptom?.Name ?? "Unknown symptom";
            if (episode != null)
            {
                episode.Status = "resolved";
                episode.ResolvedAt = DateTime.UtcNow;
            }

            // Send status update (fire-and-forget)
            clientConnection?.SendSymptomResolved(episodeId, symptomName);

            logger.LogInformation("Resolved episode {EpisodeId}", episodeId);
            return $"Resolved episode {episodeId}.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error resolving episode {EpisodeId}", episodeId);
            return $"Error resolving episode: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("RecordNegativeFinding: YOU MUST CALL THIS FUNCTION when a user explicitly denies having a symptom (e.g., 'no fever', 'I don't have nausea'). This helps rule out conditions. Parameters: symptomName (required - the symptom they don't have), episodeId (optional - link to a related episode).")]
    public async Task<string> RecordNegativeFindingAsync(
        [Description("The name of the symptom the user does not have")] string symptomName,
        [Description("Optional episode ID if this relates to a specific episode")] int? episodeId = null)
    {
        var context = GetContext();
        var clientConnection = GetClientConnection();

        try
        {
            clientConnection?.SendProcessing($"Recording negative finding for {symptomName}");

            var negativeFinding = await negativeFindingRepository.RecordNegativeFindingAsync(
                context.UserId,
                symptomName,
                episodeId);

            // Update context
            context.NegativeFindings.Add(negativeFinding);

            clientConnection?.SendCompleted($"Recorded that {symptomName} is not present");
            logger.LogInformation("Recorded negative finding for {SymptomName}", symptomName);
            return $"Recorded that user does not have {symptomName}.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error recording negative finding for {SymptomName}", symptomName);
            return $"Error recording negative finding: {ex.Message}";
        }
    }

    [KernelFunction]
    [Description("GetActiveEpisodes: CALL THIS FUNCTION to check what symptoms/episodes the user currently has active. Use this before creating new episodes to avoid duplicates. Returns a list of active episode IDs and their stages.")]
    public string GetActiveEpisodes()
    {
        var context = GetContext();
        var clientConnection = GetClientConnection();

        clientConnection?.SendProcessing("Retrieving active episodes");

        var episodes = context.ActiveEpisodes
            .Where(e => e.Status == "active")
            .Select(e => $"{e.Id}: {e.Symptom?.Name ?? "Unknown"} - {e.Stage}")
            .ToList();

        clientConnection?.SendCompleted("Retrieved active episodes");

        if (!episodes.Any())
        {
            return "No active episodes.";
        }

        return string.Join(", ", episodes);
    }

    [KernelFunction]
    [Description("GetSymptomHistory: CALL THIS FUNCTION to get past episodes for a specific symptom. Use this to understand symptom patterns over time. Parameters: symptomName (required - the symptom to get history for).")]
    public async Task<string> GetSymptomHistoryAsync(
        [Description("The name of the symptom to get history for")] string symptomName)
    {
        var context = GetContext();
        var clientConnection = GetClientConnection();

        try
        {
            clientConnection?.SendProcessing($"Retrieving history for {symptomName}");

            var episodes = await episodeRepository.GetEpisodesBySymptomAsync(context.UserId, symptomName);

            clientConnection?.SendCompleted($"Retrieved history for {symptomName}");

            if (!episodes.Any())
            {
                return $"No history found for {symptomName}.";
            }

            var history = episodes
                .Select(e => $"{e.StartedAt:yyyy-MM-dd}: {e.Stage} ({e.Status})")
                .ToList();

            return $"History for {symptomName}: " + string.Join("; ", history);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting symptom history for {SymptomName}", symptomName);
            return $"Error getting history: {ex.Message}";
        }
    }
}
