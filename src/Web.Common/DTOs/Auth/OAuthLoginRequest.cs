using System.ComponentModel.DataAnnotations;

namespace Web.Common.DTOs.Auth;

public class OAuthLoginRequest
{
    [Required(ErrorMessage = "Provider is required")]
    public required string Provider { get; set; }
    
    /// <summary>
    /// ID token for providers that use implicit flow (Google, Microsoft)
    /// </summary>
    public string? IdToken { get; set; }
    
    /// <summary>
    /// Authorization code for providers that use authorization code flow (GitHub)
    /// </summary>
    public string? AuthorizationCode { get; set; }
    
    /// <summary>
    /// Redirect URI used in the authorization request (required for authorization code flow)
    /// </summary>
    public string? RedirectUri { get; set; }
}
