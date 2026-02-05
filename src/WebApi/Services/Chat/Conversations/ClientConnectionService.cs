using WebApi.Hubs;

namespace WebApi.Services.Chat.Conversations;

/// <summary>
/// Scoped service that holds the ClientConnection for the current request.
/// Used to pass SignalR connection to plugins without direct dependency.
/// </summary>
public class ClientConnectionService
{
    public ClientConnection? CurrentConnection { get; set; }
}
