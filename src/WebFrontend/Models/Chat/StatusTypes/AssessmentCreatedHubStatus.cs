namespace WebFrontend.Models.Chat.StatusTypes;

/// <summary>
/// SignalR hub notification that an assessment was created.
/// Lightweight subset of the full AssessmentCreatedStatus DTO.
/// </summary>
public class AssessmentCreatedHubStatus : StatusInformation
{
    public override string Type => "assessment-created";

    /// <summary>
    /// The ID of the created assessment.
    /// </summary>
    public int AssessmentId { get; set; }

    /// <summary>
    /// The hypothesis of the assessment.
    /// </summary>
    public required string Hypothesis { get; set; }

    /// <summary>
    /// The confidence level (0-1).
    /// </summary>
    public decimal Confidence { get; set; }
}
