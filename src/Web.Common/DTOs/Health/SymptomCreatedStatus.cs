namespace Web.Common.DTOs.Health;

public class SymptomCreatedStatus
{
    public int SymptomId { get; set; }
    public int EpisodeId { get; set; }
    public string? SymptomName { get; set; }
    public string? SymptomDescription { get; set; }
    public string? EpisodeStage { get; set; }
    public string? EpisodeStatus { get; set; }
    public DateTime? StartedAt { get; set; }
}
