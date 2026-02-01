namespace WebFrontend.Models.Chat.StatusTypes;

/// <summary>
/// General status information for displaying simple messages.
/// </summary>
public class GeneralStatus : StatusInformation
{
    public override string Type => "general";

    /// <summary>
    /// The message to display.
    /// </summary>
    public required string Message { get; set; }
}
