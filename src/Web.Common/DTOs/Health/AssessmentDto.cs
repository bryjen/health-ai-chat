namespace Web.Common.DTOs.Health;

public class AssessmentDto
{
    public int Id { get; set; }
    public Guid ConversationId { get; set; }
    public required string Hypothesis { get; set; }
    public decimal Confidence { get; set; } // 0-1
    public List<string>? Differentials { get; set; }
    public required string Reasoning { get; set; }
    public required string RecommendedAction { get; set; } // "self-care", "see-gp", "urgent-care", "emergency"
    public List<int>? EpisodeIds { get; set; } // Backward compatibility: derived from LinkedEpisodes
    public List<AssessmentEpisodeLinkDto>? LinkedEpisodes { get; set; }
    public List<int>? NegativeFindingIds { get; set; }
    public DateTime CreatedAt { get; set; }
}
