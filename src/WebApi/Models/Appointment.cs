using System.Diagnostics.CodeAnalysis;

namespace WebApi.Models;

[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")]
[SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
public class Appointment
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string? ClinicName { get; set; }
    public DateTime? DateTime { get; set; }
    public string? Reason { get; set; }
    public required string Status { get; set; } // "Scheduled", "Completed", "Cancelled", "Pending"
    public string? Urgency { get; set; } // "Emergency", "High", "Medium", "Low"
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    // Navigation properties
    public User? User { get; set; }
}
