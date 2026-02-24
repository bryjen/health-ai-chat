namespace Web.Common.DTOs.Conversations;

public class MessageDto
{
    public string? Id { get; set; }
    public required string Role { get; set; }
    public required string Content { get; set; }
    public DateTime? CreatedAt { get; set; }
    public string? StatusInformationJson { get; set; }
}
