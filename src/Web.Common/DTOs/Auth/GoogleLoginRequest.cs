using System.ComponentModel.DataAnnotations;

namespace Web.Common.DTOs.Auth;

public class GoogleLoginRequest
{
    [Required(ErrorMessage = "ID token is required")]
    public required string IdToken { get; set; }
}
