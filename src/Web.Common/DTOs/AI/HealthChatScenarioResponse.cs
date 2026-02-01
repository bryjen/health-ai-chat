namespace Web.Common.DTOs.AI;

/// <summary>
/// Response DTO for the health chat scenario.
/// Contains the raw AI-generated response text.
/// Note: JSON parsing into HealthAssistantResponse is handled by HealthChatOrchestrator.
/// </summary>
public class HealthChatScenarioResponse
{
    /// <summary>
    /// The AI-generated response message.
    /// </summary>
    public required string Message { get; set; }
}
