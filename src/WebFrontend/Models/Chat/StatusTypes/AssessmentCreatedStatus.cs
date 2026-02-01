namespace WebFrontend.Models.Chat.StatusTypes;

/// <summary>
/// Status indicating that an assessment has been created.
/// </summary>
public class AssessmentCreatedStatus : StatusInformation
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
