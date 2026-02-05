using WebApi.Repositories;

namespace WebApi.Services.Chat.Conversations;

/// <summary>
/// Service for hydrating and managing ConversationContext instances.
/// Handles loading active episodes, recent history, and persisting changes.
/// Manages a single scoped instance per request.
/// </summary>
public class ConversationContextService(
    EpisodeRepository episodeRepository,
    SymptomRepository symptomRepository,
    NegativeFindingRepository negativeFindingRepository,
    AssessmentRepository assessmentRepository,
    ILogger<ConversationContextService> logger)
{
    private ConversationContext? _currentContext;

    /// <summary>
    /// Gets the current scoped ConversationContext instance.
    /// Throws if context has not been hydrated.
    /// </summary>
    public ConversationContext GetCurrentContext()
    {
        if (_currentContext == null)
        {
            throw new InvalidOperationException("ConversationContext has not been hydrated. Call HydrateContextAsync first.");
        }
        return _currentContext;
    }

    /// <summary>
    /// Hydrates a ConversationContext with active episodes and recent history for a user.
    /// This becomes the scoped instance for the current request.
    /// </summary>
    public async Task<ConversationContext> HydrateContextAsync(
        Guid userId,
        Guid? conversationId = null)
    {
        var context = new ConversationContext
        {
            UserId = userId,
            ConversationId = conversationId
        };

        try
        {
            // Load active episodes from last 14 days
            var activeEpisodes = await episodeRepository.GetActiveEpisodesAsync(userId, days: 14);
            context.ActiveEpisodes = activeEpisodes;

            // Load associated symptoms
            var symptomIds = activeEpisodes.Select(e => e.SymptomId).Distinct().ToList();
            var symptoms = await symptomRepository.GetSymptomsAsync(userId);
            context.ActiveSymptoms = symptoms.Where(s => symptomIds.Contains(s.Id)).ToList();

            // Build RecentEpisodesBySymptom dictionary for duplicate detection
            foreach (var episode in activeEpisodes)
            {
                if (episode.Symptom != null)
                {
                    var symptomName = episode.Symptom.Name;
                    if (!context.RecentEpisodesBySymptom.ContainsKey(symptomName) ||
                        context.RecentEpisodesBySymptom[symptomName].StartedAt < episode.StartedAt)
                    {
                        context.RecentEpisodesBySymptom[symptomName] = episode;
                    }
                }
            }

            // Load recent negative findings (last 7 days)
            var negativeFindings = await negativeFindingRepository.GetNegativeFindingsAsync(
                userId,
                sinceDate: DateTime.UtcNow.AddDays(-7));
            context.NegativeFindings = negativeFindings;

            // Load current assessment if conversation exists
            if (conversationId.HasValue)
            {
                var assessment = await assessmentRepository.GetAssessmentByConversationAsync(conversationId.Value);
                if (assessment != null)
                {
                    context.CurrentAssessment = assessment;
                    // Set phase based on assessment existence
                    context.Phase = ConversationPhase.Assessing;
                }
            }

            logger.LogDebug(
                "Hydrated context for user {UserId}: {EpisodeCount} episodes, {SymptomCount} symptoms, {NegativeFindingCount} negative findings",
                userId,
                context.ActiveEpisodes.Count,
                context.ActiveSymptoms.Count,
                context.NegativeFindings.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error hydrating context for user {UserId}", userId);
            throw;
        }

        // Store as the scoped instance for this request
        _currentContext = context;
        return context;
    }

    /// <summary>
    /// Updates the context with changes made during AI function calls.
    /// This is called after plugins modify episodes/assessments.
    /// </summary>
    public void UpdateContext(ConversationContext context, object? changes)
    {
        // Context is updated in-place by plugins that have access to it
        // This method can be used for explicit updates if needed
        if (changes != null)
        {
            logger.LogDebug("Updating context with changes");
        }
    }

    /// <summary>
    /// Flushes any pending changes from the context to the database.
    /// Since plugins persist changes directly, this is mainly for validation/logging.
    /// </summary>
    public async Task FlushContextAsync(ConversationContext context)
    {
        // Most changes are already persisted by repositories called from plugins
        // This method can be used for final validation or cleanup if needed
        logger.LogDebug("Flushing context - changes should already be persisted");
        await Task.CompletedTask;
    }
}
