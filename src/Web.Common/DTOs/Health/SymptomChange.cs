namespace Web.Common.DTOs.Health;

public class SymptomChange
{
    public required string Symptom { get; set; }
    public required string Action { get; set; } // "added", "removed", "updated"
}
