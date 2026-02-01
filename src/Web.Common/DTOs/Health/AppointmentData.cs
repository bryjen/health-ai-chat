namespace Web.Common.DTOs.Health;

public class AppointmentData
{
    public bool Needed { get; set; }
    public string Urgency { get; set; } = string.Empty; // "Emergency", "High", "Medium", "Low", "None"
    public List<string> Symptoms { get; set; } = new();
    public int? Duration { get; set; } // in minutes
    public bool ReadyToBook { get; set; }
    public bool FollowUpNeeded { get; set; }
    public List<string> NextQuestions { get; set; } = new();
    public DateTime? PreferredTime { get; set; }
    public string? EmergencyAction { get; set; }
}
