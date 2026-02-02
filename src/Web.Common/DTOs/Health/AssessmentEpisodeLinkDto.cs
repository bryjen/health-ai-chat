namespace Web.Common.DTOs.Health;

public class AssessmentEpisodeLinkDto
{
    public int EpisodeId { get; set; }
    public decimal Weight { get; set; }
    public string? Reasoning { get; set; }
    public string? EpisodeName { get; set; }
}
