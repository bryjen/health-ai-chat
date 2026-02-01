namespace Web.Common.DTOs.Health;

public class HealthAssistantResponse
{
    public required string Message { get; set; }
    public AppointmentData? Appointment { get; set; }
    public List<SymptomChange>? SymptomChanges { get; set; }
}
