namespace Web.Common.DTOs.Health;

public class AssessmentCreatedStatus
{
    public int AssessmentId { get; set; }
    public string? Hypothesis { get; set; }
    public float Confidence { get; set; }
    public List<string>? Differentials { get; set; }
    public string? Reasoning { get; set; }
    public string? RecommendedAction { get; set; }
}
