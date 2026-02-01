using System.ComponentModel;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using WebApi.Models;
using WebApi.Repositories;
using WebApi.Services.Chat;

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
    private ConversationContext? _context;
    private Guid _userId;

    /// <summary>
    /// Sets the current conversation context and user ID for this plugin instance.
    /// Called before kernel function execution.
    /// </summary>
    public void SetContext(ConversationContext context, Guid userId)
    {
        _context = context;
        _userId = userId;
    }

    [KernelFunction]
    [Description("CreateSymptomWithEpisode: Creates a new symptom type and an associated episode when a user mentions a symptom. Returns the created episode ID.")]
    public async Task<string> CreateSymptomWithEpisodeAsync(
        [Description("The name of the symptom")] string name,
        [Description("Optional description of the symptom")] string? description = null)
    {
        if (_context == null)
        {
            throw new InvalidOperationException("Context not set. Call SetContext before using plugin functions.");
        }

        try
        {
            // Get or create symptom type
            var symptom = await symptomRepository.GetOrCreateSymptomAsync(_userId, name, description);
            
            // Check if there's a recent episode for this symptom
            if (_context.RecentEpisodesBySymptom.TryGetValue(name, out var recentEpisode))
            {
                logger.LogInformation("Found recent episode {EpisodeId} for symptom {SymptomName}", recentEpisode.Id, name);
                return $"Found existing episode {recentEpisode.Id} for {name}. Use UpdateEpisode to add details.";
            }

            // Create new episode
            var episode = await episodeRepository.CreateEpisodeAsync(
                _userId,
                symptom.Id,
                DateTime.UtcNow,
                stage: "mentioned",
                status: "active");

            // Update context
            _context.ActiveEpisodes.Add(episode);
            _context.ActiveSymptoms.Add(symptom);
            _context.RecentEpisodesBySymptom[name] = episode;

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
    [Description("UpdateEpisode: Updates an episode with additional details like severity, location, frequency, triggers, relievers, or pattern. Automatically advances the episode stage based on how many fields are filled.")]
    public async Task<string> UpdateEpisodeAsync(
        [Description("The ID of the episode to update")] int episodeId,
        [Description("Severity on a scale of 1-10")] int? severity = null,
        [Description("Location where the symptom occurs")] string? location = null,
        [Description("Frequency: constant, intermittent, or occasional")] string? frequency = null,
        [Description("Comma-separated list of triggers")] string? triggers = null,
        [Description("Comma-separated list of relievers")] string? relievers = null,
        [Description("Pattern description (e.g., 'worse in morning')")] string? pattern = null)
    {
        if (_context == null)
        {
            throw new InvalidOperationException("Context not set.");
        }

        try
        {
            var triggersList = triggers != null ? triggers.Split(',').Select(t => t.Trim()).ToList() : null;
            var relieversList = relievers != null ? relievers.Split(',').Select(r => r.Trim()).ToList() : null;

            var episode = await episodeRepository.UpdateEpisodeAsync(
                episodeId,
                severity,
                location,
                frequency,
                triggersList,
                relieversList,
                pattern);

            if (episode == null)
            {
                return $"Episode {episodeId} not found.";
            }

            // Update context
            var contextEpisode = _context.ActiveEpisodes.FirstOrDefault(e => e.Id == episodeId);
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
        if (_context == null)
        {
            throw new InvalidOperationException("Context not set.");
        }

        try
        {
            var success = await episodeRepository.LinkEpisodesAsync(episodeId, relatedEpisodeId);
            if (!success)
            {
                return $"Episode {episodeId} not found.";
            }

            // Update context
            var episode = _context.ActiveEpisodes.FirstOrDefault(e => e.Id == episodeId);
            if (episode != null)
            {
                episode.Stage = "linked";
            }

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
        if (_context == null)
        {
            throw new InvalidOperationException("Context not set.");
        }

        try
        {
            var success = await episodeRepository.ResolveEpisodeAsync(episodeId);
            if (!success)
            {
                return $"Episode {episodeId} not found.";
            }

            // Update context
            var episode = _context.ActiveEpisodes.FirstOrDefault(e => e.Id == episodeId);
            if (episode != null)
            {
                episode.Status = "resolved";
                episode.ResolvedAt = DateTime.UtcNow;
            }

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
    [Description("RecordNegativeFinding: Records a negative finding when a user explicitly denies having a symptom (e.g., 'no fever', 'I don't have nausea').")]
    public async Task<string> RecordNegativeFindingAsync(
        [Description("The name of the symptom the user does not have")] string symptomName,
        [Description("Optional episode ID if this relates to a specific episode")] int? episodeId = null)
    {
        if (_context == null)
        {
            throw new InvalidOperationException("Context not set.");
        }

        try
        {
            var negativeFinding = await negativeFindingRepository.RecordNegativeFindingAsync(
                _userId,
                symptomName,
                episodeId);

            // Update context
            _context.NegativeFindings.Add(negativeFinding);

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
    [Description("GetActiveEpisodes: Returns a list of active episodes from the conversation context.")]
    public string GetActiveEpisodes()
    {
        if (_context == null)
        {
            throw new InvalidOperationException("Context not set.");
        }

        var episodes = _context.ActiveEpisodes
            .Where(e => e.Status == "active")
            .Select(e => $"{e.Id}: {e.Symptom?.Name ?? "Unknown"} - {e.Stage}")
            .ToList();

        if (!episodes.Any())
        {
            return "No active episodes.";
        }

        return string.Join(", ", episodes);
    }

    [KernelFunction]
    [Description("GetSymptomHistory: Returns the history of episodes for a specific symptom type.")]
    public async Task<string> GetSymptomHistoryAsync(
        [Description("The name of the symptom to get history for")] string symptomName)
    {
        if (_context == null)
        {
            throw new InvalidOperationException("Context not set.");
        }

        try
        {
            var episodes = await episodeRepository.GetEpisodesBySymptomAsync(_userId, symptomName);
            
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
