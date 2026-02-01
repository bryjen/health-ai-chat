namespace WebFrontend.Models.Chat.StatusTypes;

/// <summary>
/// Status indicating that an assessment is being generated.
/// </summary>
public class AssessmentGeneratingStatus : StatusInformation
{
    public override string Type => "assessment-generating";

    /// <summary>
    /// The message to display.
    /// </summary>
    public required string Message { get; set; }
}
