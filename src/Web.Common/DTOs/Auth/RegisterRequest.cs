using System.ComponentModel.DataAnnotations;

namespace Web.Common.DTOs.Auth;

public class RegisterRequest
{
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    [StringLength(255, ErrorMessage = "Email must not exceed 255 characters")]
    public required string Email { get; set; }
    
    [Required(ErrorMessage = "Password is required")]
    [StringLength(100, MinimumLength = 12, ErrorMessage = "Password must be between 12 and 100 characters")]
    public required string Password { get; set; }

    [StringLength(100, ErrorMessage = "First name must be at most 100 characters")]
    public string? FirstName { get; set; }

    [StringLength(100, ErrorMessage = "Last name must be at most 100 characters")]
    public string? LastName { get; set; }
}


