namespace Web.Common.DTOs.Health;

public class SymptomDto
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int EpisodeCount { get; set; }
}
