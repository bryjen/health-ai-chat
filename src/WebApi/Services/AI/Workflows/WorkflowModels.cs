using WebApi.Models;

namespace WebApi.Services.AI.Workflows;

/// <summary>
/// Request model for creating an assessment.
/// </summary>
public record CreateAssessmentRequest(
    string Hypothesis,
    decimal Confidence,
    List<string>? Differentials = null,
    string? Reasoning = null,
    string? RecommendedAction = null,
    List<int>? NegativeFindingIds = null);

/// <summary>
/// Result from creating an assessment.
/// </summary>
public class AssessmentResult
{
    public string NextRecommendedAction { get; set; } = string.Empty;
    public Assessment? CreatedAssessment { get; set; }
    public Assessment? UpdatedAssessment { get; set; }
    public int? CompletedAssessmentId { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Result from symptom/episode operations.
/// </summary>
public class SymptomEpisodeResult
{
    public string NextRecommendedAction { get; set; } = string.Empty;
    public Episode? CreatedEpisode { get; set; }
    public Episode? UpdatedEpisode { get; set; }
    public Episode? ResolvedEpisode { get; set; }
    public Episode? Episode { get; set; }
    public string? Message { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Result from recording negative findings.
/// </summary>
public class NegativeFindingResult
{
    public string NextRecommendedAction { get; set; } = string.Empty;
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public int? EpisodeId { get; set; }
    public string SymptomName { get; set; } = string.Empty;
    public DateTime ReportedAt { get; set; }
}

/// <summary>
/// Result from getting active episodes.
/// </summary>
public class ActiveEpisodesResult
{
    public string NextRecommendedAction { get; set; } = string.Empty;
    public List<Episode> ActiveEpisodes { get; set; } = new();
}

/// <summary>
/// Result from getting symptom history.
/// </summary>
public class SymptomHistoryResult
{
    public string NextRecommendedAction { get; set; } = string.Empty;
    public List<Episode> SymptomHistory { get; set; } = new();
    public string SymptomName { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Extracted symptom data from LLM.
/// </summary>
public class SymptomData
{
    public string Hypothesis { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public List<string>? Differentials { get; set; }
    public string? Reasoning { get; set; }
    public string RecommendedAction { get; set; } = "see-gp";
}

/// <summary>
/// Detected symptoms from user message.
/// </summary>
public class DetectedSymptoms
{
    public List<string> Symptoms { get; set; } = new();
}

/// <summary>
/// Episode weight mapping for assessment updates.
/// </summary>
public class EpisodeWeight
{
    public int EpisodeId { get; set; }
    public decimal Weight { get; set; }
}

/// <summary>
/// Workflow context state keys.
/// </summary>
public static class WorkflowStateKeys
{
    public const string UserId = "userId";
    public const string ConversationId = "conversationId";
    public const string UserMessage = "userMessage";
    public const string Intent = "intent";
    public const string Symptoms = "symptoms";
    public const string AssessmentId = "assessmentId";
    public const string Response = "response";
    public const string ActiveEpisodes = "activeEpisodes";
}
