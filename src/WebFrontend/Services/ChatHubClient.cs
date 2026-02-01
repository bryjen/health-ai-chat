using Microsoft.AspNetCore.SignalR.Client;
using Web.Common.DTOs.Health;
using WebApi.ApiWrapper.Services;

namespace WebFrontend.Services;

/// <summary>
/// Concrete client for the health chat SignalR hub.
/// Manages the hub connection and sending messages.
/// </summary>
public class ChatHubClient : IAsyncDisposable
{
    private readonly ITokenProvider _tokenProvider;
    private HubConnection? _hubConnection;

    // TODO: consider moving this to configuration if needed
    private const string HubUrl = "https://localhost:7265/hubs/chat";

    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    public ChatHubClient(ITokenProvider tokenProvider)
    {
        _tokenProvider = tokenProvider;
    }

    public async Task ConnectAsync()
    {
        if (_hubConnection is { State: HubConnectionState.Connected })
        {
            return;
        }

        if (_hubConnection == null)
        {
            _hubConnection = new HubConnectionBuilder()
                .WithUrl(HubUrl, options =>
                {
                    options.AccessTokenProvider = async () => await _tokenProvider.GetTokenAsync();
                })
                .WithAutomaticReconnect()
                .Build();
        }

        if (_hubConnection.State == HubConnectionState.Disconnected)
        {
            await _hubConnection.StartAsync();
        }
    }

    public async Task<HealthChatResponse> SendMessageAsync(string message, Guid? conversationId)
    {
        if (_hubConnection == null || _hubConnection.State != HubConnectionState.Connected)
        {
            throw new InvalidOperationException("Chat connection is not established.");
        }

        return await _hubConnection.InvokeAsync<HealthChatResponse>("SendMessage", message, conversationId);
    }

    public async Task DisconnectAsync()
    {
        if (_hubConnection != null && _hubConnection.State != HubConnectionState.Disconnected)
        {
            try
            {
                await _hubConnection.StopAsync();
            }
            catch
            {
                // Ignore errors when disconnecting
            }
        }
    }
    
    public async ValueTask DisposeAsync()
    {
        if (_hubConnection is not null)
        {
            await _hubConnection.DisposeAsync();
        }
    }
}

