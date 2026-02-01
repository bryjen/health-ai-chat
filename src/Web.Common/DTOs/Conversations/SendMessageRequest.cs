using System.ComponentModel.DataAnnotations;

namespace Web.Common.DTOs.Conversations;

public class SendMessageRequest
{
    [Required(ErrorMessage = "Content is required")]
    [StringLength(10000, ErrorMessage = "Content must not exceed 10000 characters")]
    public required string Content { get; set; }
    
    public Guid? ConversationId { get; set; }
}
