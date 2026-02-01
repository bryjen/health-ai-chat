namespace Web.Common.DTOs.Health;

public class EpisodeDto
{
    public int Id { get; set; }
    public int SymptomId { get; set; }
    public required string SymptomName { get; set; }
    public string? SymptomDescription { get; set; }
    public required string Stage { get; set; } // "mentioned", "explored", "characterized", "linked"
    public required string Status { get; set; } // "active", "resolved", "chronic"
    public DateTime StartedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public int? Severity { get; set; } // 1-10 scale
    public string? Location { get; set; }
    public string? Frequency { get; set; } // "constant", "intermittent", "occasional"
    public List<string>? Triggers { get; set; }
    public List<string>? Relievers { get; set; }
    public string? Pattern { get; set; }
    public List<EpisodeTimelineEntryDto>? Timeline { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class EpisodeTimelineEntryDto
{
    public DateTime Date { get; set; }
    public int? Severity { get; set; }
    public string? Notes { get; set; }
}
