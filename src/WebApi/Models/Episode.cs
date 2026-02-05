using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace WebApi.Models;

[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")]
[SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
public class Episode
{
    public int Id { get; set; }
    public int SymptomId { get; set; }
    public Guid UserId { get; set; }
    
    // Lifecycle
    public required string Stage { get; set; } // "mentioned", "explored", "characterized", "linked"
    public required string Status { get; set; } // "active", "resolved", "chronic"
    
    // Temporal
    public DateTime StartedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    
    // Details (extracted during exploration)
    public int? Severity { get; set; } // 1-10 scale
    public string? Location { get; set; }
    public string? Frequency { get; set; } // "constant", "intermittent", "occasional"
    public List<string>? Triggers { get; set; }
    public List<string>? Relievers { get; set; }
    public string? Pattern { get; set; } // "worse in morning", "after eating"
    
    // Timeline entries for ongoing tracking (stored as JSON)
    public List<EpisodeTimelineEntry>? Timeline { get; set; }
    
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    // Navigation properties
    public Symptom? Symptom { get; set; } // Keep this - we need Symptom.Name, but Symptom.Episodes is ignored
    [JsonIgnore]
    public User? User { get; set; }
}

public class EpisodeTimelineEntry
{
    public DateTime Date { get; set; }
    public int? Severity { get; set; }
    public string? Notes { get; set; }
}
