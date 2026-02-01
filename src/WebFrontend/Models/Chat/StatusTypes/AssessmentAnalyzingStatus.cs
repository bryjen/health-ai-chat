namespace WebFrontend.Models.Chat.StatusTypes;

/// <summary>
/// Status indicating that the model is analyzing an assessment.
/// </summary>
public class AssessmentAnalyzingStatus : StatusInformation
{
    public override string Type => "assessment-analyzing";

    /// <summary>
    /// The message to display.
    /// </summary>
    public required string Message { get; set; }
}
