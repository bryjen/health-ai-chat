namespace Web.Common.DTOs.Auth;

public class UserDto
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public DateTime CreatedAt { get; set; }
}


