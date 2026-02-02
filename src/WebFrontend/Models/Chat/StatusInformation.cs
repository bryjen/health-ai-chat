namespace WebFrontend.Models.Chat;

/// <summary>
/// Abstract base class for status information displayed in chat messages.
/// Provides extensible system for different types of status bubbles.
/// </summary>
public abstract class StatusInformation
{
    /// <summary>
    /// Gets the type identifier for this status (e.g., "general", "symptom-added", "assessment-generating").
    /// </summary>
    public abstract string Type { get; }

    /// <summary>
    /// Timestamp when this status was created.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
