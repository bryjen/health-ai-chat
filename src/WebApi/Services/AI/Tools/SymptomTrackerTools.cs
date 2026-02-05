using WebApi.Hubs;
using WebApi.Models;
using WebApi.Repositories;
using WebApi.Services.AI.Workflows;
using WebApi.Services.Chat.Conversations;

namespace WebApi.Services.AI.Tools;

/// <summary>
/// Agent Framework tools for symptom tracking operations.
/// Converted from SymptomTrackerPlugin - provides tools that workflows can call.
/// </summary>
public class SymptomTrackerTools(
    SymptomRepository symptomRepository,
    EpisodeRepository episodeRepository,
    NegativeFindingRepository negativeFindingRepository,
    ConversationContextService contextService,
    ILogger<SymptomTrackerTools> logger)
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

    /// <summary>
    /// Creates a symptom with an episode. Call this immediately when a user reports ANY symptom.
    /// </summary>
    public async Task<SymptomEpisodeResult> CreateSymptomWithEpisodeAsync(
        string name,
        string? description = null)
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
                return new SymptomEpisodeResult
                {
                    NextRecommendedAction = "Continue",
                    Message = $"Found existing episode {recentEpisode.Id} for {name}. Use UpdateEpisode to add details.",
                    Episode = recentEpisode
                };
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
            return new SymptomEpisodeResult
            {
                NextRecommendedAction = "Continue",
                CreatedEpisode = episode
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating symptom with episode for {SymptomName}", name);
            return new SymptomEpisodeResult
            {
                NextRecommendedAction = "SubmitFinalResponse",
                ErrorMessage = $"Error creating episode: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Updates an episode with additional details (severity, location, frequency, triggers, relievers, pattern).
    /// </summary>
    public async Task<SymptomEpisodeResult> UpdateEpisodeAsync(
        int episodeId,
        int? severity = null,
        string? location = null,
        string? frequency = null,
        List<string>? triggers = null,
        List<string>? relievers = null,
        string? pattern = null)
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
                return new SymptomEpisodeResult
                {
                    NextRecommendedAction = "SubmitFinalResponse",
                    ErrorMessage = $"Episode {episodeId} not found."
                };
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
            return new SymptomEpisodeResult
            {
                NextRecommendedAction = "Continue",
                UpdatedEpisode = episode
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating episode {EpisodeId}", episodeId);
            return new SymptomEpisodeResult
            {
                NextRecommendedAction = "SubmitFinalResponse",
                ErrorMessage = $"Error updating episode: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Links an episode to another related episode, marking it as 'linked' stage.
    /// </summary>
    public async Task<SymptomEpisodeResult> LinkEpisodeToExistingAsync(
        int episodeId,
        int relatedEpisodeId)
    {
        var context = GetContext();
        var clientConnection = GetClientConnection();

        try
        {
            clientConnection?.SendProcessing("Linking episodes");

            var success = await episodeRepository.LinkEpisodesAsync(episodeId, relatedEpisodeId);
            if (!success)
            {
                return new SymptomEpisodeResult
                {
                    NextRecommendedAction = "SubmitFinalResponse",
                    ErrorMessage = $"Episode {episodeId} not found."
                };
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
            return new SymptomEpisodeResult
            {
                NextRecommendedAction = "Continue",
                Message = $"Linked episode {episodeId} to episode {relatedEpisodeId}.",
                Episode = episode
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error linking episodes {EpisodeId} to {RelatedEpisodeId}", episodeId, relatedEpisodeId);
            return new SymptomEpisodeResult
            {
                NextRecommendedAction = "SubmitFinalResponse",
                ErrorMessage = $"Error linking episodes: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Marks an episode as resolved, setting its status to 'resolved' and recording the resolution date.
    /// </summary>
    public async Task<SymptomEpisodeResult> ResolveEpisodeAsync(int episodeId)
    {
        var context = GetContext();
        var clientConnection = GetClientConnection();

        try
        {
            var success = await episodeRepository.ResolveEpisodeAsync(episodeId);
            if (!success)
            {
                return new SymptomEpisodeResult
                {
                    NextRecommendedAction = "SubmitFinalResponse",
                    ErrorMessage = $"Episode {episodeId} not found."
                };
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
            return new SymptomEpisodeResult
            {
                NextRecommendedAction = "Continue",
                ResolvedEpisode = episode
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error resolving episode {EpisodeId}", episodeId);
            return new SymptomEpisodeResult
            {
                NextRecommendedAction = "SubmitFinalResponse",
                ErrorMessage = $"Error resolving episode: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Records a negative finding when a user explicitly denies having a symptom.
    /// </summary>
    public async Task<NegativeFindingResult> RecordNegativeFindingAsync(
        string symptomName,
        int? episodeId = null)
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
            return new NegativeFindingResult
            {
                NextRecommendedAction = "Continue",
                Id = negativeFinding.Id,
                UserId = negativeFinding.UserId,
                EpisodeId = negativeFinding.EpisodeId,
                SymptomName = negativeFinding.SymptomName,
                ReportedAt = negativeFinding.ReportedAt
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error recording negative finding for {SymptomName}", symptomName);
            throw; // Let workflow handle errors
        }
    }

    /// <summary>
    /// Gets active episodes for the current user.
    /// </summary>
    public ActiveEpisodesResult GetActiveEpisodes()
    {
        var context = GetContext();
        var clientConnection = GetClientConnection();

        clientConnection?.SendProcessing("Retrieving active episodes");

        var episodes = context.ActiveEpisodes
            .Where(e => e.Status == "active")
            .ToList();

        clientConnection?.SendCompleted("Retrieved active episodes");

        return new ActiveEpisodesResult
        {
            NextRecommendedAction = "Continue",
            ActiveEpisodes = episodes
        };
    }

    /// <summary>
    /// Gets past episodes for a specific symptom.
    /// </summary>
    public async Task<SymptomHistoryResult> GetSymptomHistoryAsync(string symptomName)
    {
        var context = GetContext();
        var clientConnection = GetClientConnection();

        try
        {
            clientConnection?.SendProcessing($"Retrieving history for {symptomName}");

            var episodes = await episodeRepository.GetEpisodesBySymptomAsync(context.UserId, symptomName);

            clientConnection?.SendCompleted($"Retrieved history for {symptomName}");

            return new SymptomHistoryResult
            {
                NextRecommendedAction = "Continue",
                SymptomHistory = episodes,
                SymptomName = symptomName
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting symptom history for {SymptomName}", symptomName);
            return new SymptomHistoryResult
            {
                NextRecommendedAction = "SubmitFinalResponse",
                SymptomName = symptomName,
                ErrorMessage = $"Error getting history: {ex.Message}"
            };
        }
    }
}
