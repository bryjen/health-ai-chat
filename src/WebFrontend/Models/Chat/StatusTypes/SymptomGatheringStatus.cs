namespace WebFrontend.Models.Chat.StatusTypes;

/// <summary>
/// Status information when gathering symptom details.
/// </summary>
public class SymptomGatheringStatus : StatusInformation
{
    public override string Type => "symptom-gathering";

    /// <summary>
    /// The message to display (e.g., "Gathering symptom details").
    /// </summary>
    public required string Message { get; set; }
}
