using WebApi.Models;

namespace WebApi.Services.Chat.Conversations;

/// <summary>
/// Non-persisted class that holds working memory for a conversation session.
/// This context is hydrated at the start of a conversation and updated as the AI interacts.
/// </summary>
public class ConversationContext
{
    // Request identifiers
    public Guid UserId { get; set; }
    public Guid? ConversationId { get; set; }

    // Working memory
    public List<Symptom> ActiveSymptoms { get; set; } = new();
    public List<Episode> ActiveEpisodes { get; set; } = new();
    public List<NegativeFinding> NegativeFindings { get; set; } = new();

    // Session state
    public Assessment? CurrentAssessment { get; set; }
    public ConversationPhase Phase { get; set; } = ConversationPhase.Gathering;
    public List<string> PendingQuestions { get; set; } = new();

    // For linking decisions - maps symptom name to most recent episode
    public Dictionary<string, Episode> RecentEpisodesBySymptom { get; set; } = new();
}

public enum ConversationPhase
{
    Gathering,    // Still collecting symptoms, asking follow-ups
    Assessing,    // Enough info, forming/sharing assessment
    Recommending  // Assessment complete, discussing next steps
}
