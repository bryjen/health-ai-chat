using System.Diagnostics.CodeAnalysis;

namespace WebApi.Models;

[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")]
[SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
public class Assessment
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ConversationId { get; set; }
    
    public required string Hypothesis { get; set; }
    public decimal Confidence { get; set; } // 0-1
    public List<string>? Differentials { get; set; } // Alternative diagnoses
    public required string Reasoning { get; set; }
    public required string RecommendedAction { get; set; } // "self-care", "see-gp", "urgent-care", "emergency"
    
    // Links (stored as JSON arrays of IDs)
    public List<int>? NegativeFindingIds { get; set; }
    
    public DateTime CreatedAt { get; set; }
    
    // Navigation properties
    public User? User { get; set; }
    public Conversation? Conversation { get; set; }
    public ICollection<AssessmentEpisodeLink> LinkedEpisodes { get; set; } = new List<AssessmentEpisodeLink>();
}
