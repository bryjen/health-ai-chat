namespace WebFrontend.Models.Chat.StatusTypes;

/// <summary>
/// Status information when a symptom episode has been added.
/// </summary>
public class SymptomAddedStatus : StatusInformation
{
    public override string Type => "symptom-added";

    /// <summary>
    /// Name of the symptom that was added.
    /// </summary>
    public required string SymptomName { get; set; }

    /// <summary>
    /// Optional location where the symptom occurs.
    /// </summary>
    public string? Location { get; set; }

    /// <summary>
    /// ID of the episode that was created.
    /// </summary>
    public int EpisodeId { get; set; }
}
