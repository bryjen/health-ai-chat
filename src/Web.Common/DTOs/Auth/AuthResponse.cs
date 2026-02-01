namespace Web.Common.DTOs.Auth;

public class AuthResponse
{
    public required UserDto User { get; set; }
    public required string AccessToken { get; set; }
    public required string RefreshToken { get; set; }
    public DateTime AccessTokenExpiresAt { get; set; }
    public DateTime RefreshTokenExpiresAt { get; set; }
}


