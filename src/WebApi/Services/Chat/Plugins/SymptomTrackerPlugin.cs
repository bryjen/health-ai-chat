using System.ComponentModel;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Web.Common.DTOs.Health;
using WebApi.Services.Chat.Response;
using WebApi.Services.Data;

// ReSharper disable MemberCanBePrivate.Global

namespace WebApi.Services.Chat.Plugins;

public sealed class SymptomTrackerPlugin(
    IOptions<JsonOptions> jsonOptions,
    ResponseWriter responseWriter,  // primarly used to emit "status updated"; also ensures that the model isn't generating/emitting anything
    AiState aiState,
    SymptomService symptomService,
    EpisodeService episodeService,
    ILogger<SymptomTrackerPlugin> logger)
{
    [Description("Create a new symptom and its first episode.")]
    public async Task<string> CreateSymptomWithEpisodeAsync(
        [Description("Symptom name (e.g., 'headache', 'cough')")] string name,
        [Description("Optional description of the symptom")] string? details = null)
    {
        try
        {
            var symptom = await symptomService.GetOrCreateSymptomAsync(aiState.UserId, name, details);
            var episode = await episodeService.CreateEpisodeAsync(aiState.UserId, symptom.Id, DateTime.UtcNow);

            var status = new SymptomCreatedStatus
            {
                SymptomId          = symptom.Id,
                EpisodeId          = episode.Id,
                SymptomName        = symptom.Name,
                SymptomDescription = symptom.Description,
                EpisodeStage       = episode.Stage,
                EpisodeStatus      = episode.Status,
                StartedAt          = episode.StartedAt,
            };
            await responseWriter.EmitRawAsync(
                "SymptomCreated",
                JsonSerializer.Serialize(status, jsonOptions.Value.JsonSerializerOptions),
                CancellationToken.None);

            logger.LogInformation("Created symptom '{Name}' with episode {EpisodeId}", name, episode.Id);
            return $"Created episode {episode.Id} for symptom '{name}'.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating symptom with episode");
            return $"Error: {ex.Message}";
        }
    }

    [Description("Update an episode with details like severity, location, triggers, etc.")]
    public async Task<string> UpdateEpisodeAsync(
        [Description("Episode ID")] int episodeId,
        [Description("Severity (1-10), or -1 to leave unchanged")] float severity = -1,
        [Description("Location on body")] string? location = null,
        [Description("How often it occurs")] string? frequency = null,
        [Description("Comma-separated list of triggers")] string? triggers = null,
        [Description("Comma-separated list of relievers")] string? relievers = null,
        [Description("Occurrence pattern (e.g., 'worse at night')")] string? occurrencePattern = null)
    {
        try
        {
            var triggersList = triggers?.Split(',').Select(t => t.Trim()).ToList();
            var relieversList = relievers?.Split(',').Select(r => r.Trim()).ToList();

            var updated = await episodeService.UpdateEpisodeAsync(
                episodeId,
                severity >= 0 ? (int?)severity : null,
                location,
                frequency,
                triggersList,
                relieversList,
                occurrencePattern);

            if (updated == null)
                return $"Episode {episodeId} not found.";

            logger.LogInformation("Updated episode {EpisodeId}", episodeId);
            return $"Updated episode {episodeId}.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating episode");
            return $"Error: {ex.Message}";
        }
    }

    [Description("Get all active episodes for the user.")]
    public async Task<string> GetActiveEpisodesAsync()
    {
        try
        {
            var episodes = await episodeService.GetActiveEpisodesAsync(aiState.UserId);

            if (!episodes.Any())
                return "No active episodes.";

            var summary = string.Join("\n", episodes.Select(e =>
                $"- Episode {e.Id}: {e.Symptom?.Name ?? "Unknown"} (stage: {e.Stage}, severity: {e.Severity ?? 0}/10)"));

            return $"Active episodes:\n{summary}";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting active episodes");
            return $"Error: {ex.Message}";
        }
    }

    [Description("Mark an episode as resolved.")]
    public async Task<string> ResolveEpisodeAsync(
        [Description("Episode ID")] int episodeId)
    {
        try
        {
            var success = await episodeService.ResolveEpisodeAsync(episodeId);

            if (!success)
                return $"Episode {episodeId} not found.";

            logger.LogInformation("Resolved episode {EpisodeId}", episodeId);
            return $"Marked episode {episodeId} as resolved.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error resolving episode");
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Returns the functions provided by this plugin.
    /// </summary>
    public IEnumerable<AITool> AsAiTools()
    {
        yield return AIFunctionFactory.Create(CreateSymptomWithEpisodeAsync);
        yield return AIFunctionFactory.Create(UpdateEpisodeAsync);
        yield return AIFunctionFactory.Create(GetActiveEpisodesAsync);
        yield return AIFunctionFactory.Create(ResolveEpisodeAsync);
    }
}
