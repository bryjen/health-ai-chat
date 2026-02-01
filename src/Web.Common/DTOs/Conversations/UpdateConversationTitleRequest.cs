using System.ComponentModel.DataAnnotations;

namespace Web.Common.DTOs.Conversations;

public class UpdateConversationTitleRequest
{
    [Required(ErrorMessage = "Title is required")]
    [StringLength(200, ErrorMessage = "Title must not exceed 200 characters")]
    public required string Title { get; set; }
}
