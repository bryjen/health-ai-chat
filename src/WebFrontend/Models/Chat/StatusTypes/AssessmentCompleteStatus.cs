namespace WebFrontend.Models.Chat.StatusTypes;

/// <summary>
/// Status indicating that an assessment has been completed.
/// </summary>
public class AssessmentCompleteStatus : StatusInformation
{
    public override string Type => "assessment-complete";

    /// <summary>
    /// The message to display.
    /// </summary>
    public required string Message { get; set; }
}
