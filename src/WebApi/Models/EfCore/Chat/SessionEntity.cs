namespace WebApi.Models.EfCore.Chat;

public class SessionEntity
{
    public string Id { get; set; } = null!;
    public string SerializedState { get; set; } = null!;
    public Guid? UserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
