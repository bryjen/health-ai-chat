using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using Web.Common.DTOs.Health;
using WebFrontend.Models.Chat;
using WebApi.ApiWrapper.Services;

namespace WebFrontend.Services;

/// <summary>
/// Concrete client for the health chat SignalR hub.
/// Manages the hub connection and sending messages.
/// </summary>
public class ChatHubClient : IAsyncDisposable
{
    private readonly ITokenProvider _tokenProvider;
    private readonly IJSRuntime? _jsRuntime;
    private HubConnection? _hubConnection;

    // TODO: consider moving this to configuration if needed
    private const string HubUrl = "https://localhost:7265/hubs/chat";

    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    public event Func<StatusInformation, Task>? StatusUpdateReceived;

    public ChatHubClient(ITokenProvider tokenProvider, IJSRuntime? jsRuntime = null)
    {
        _tokenProvider = tokenProvider;
        _jsRuntime = jsRuntime;
    }

    private async Task LogToConsoleAsync(string message)
    {
        try
        {
            if (_jsRuntime != null)
            {
                await _jsRuntime.InvokeVoidAsync("console.log", message);
            }
        }
        catch
        {
            // Ignore JS errors - logging is not critical
        }
    }

    public async Task ConnectAsync()
    {
        if (_hubConnection is { State: HubConnectionState.Connected })
        {
            await LogToConsoleAsync("[SignalR] Already connected");
            return;
        }

        if (_hubConnection == null)
        {
            await LogToConsoleAsync("[SignalR] Creating new hub connection");
            _hubConnection = new HubConnectionBuilder()
                .WithUrl(HubUrl, options =>
                {
                    options.AccessTokenProvider = async () => await _tokenProvider.GetTokenAsync();
                })
                .WithAutomaticReconnect()
                .Build();
            
            // Log connection state changes
            _hubConnection.Closed += async (error) =>
            {
                await LogToConsoleAsync($"[SignalR] Connection closed. Error: {error?.Message ?? "None"}");
            };
            
            _hubConnection.Reconnecting += async (error) =>
            {
                await LogToConsoleAsync($"[SignalR] Reconnecting. Error: {error?.Message ?? "None"}");
            };
            
            _hubConnection.Reconnected += async (connectionId) =>
            {
                await LogToConsoleAsync($"[SignalR] Reconnected. ConnectionId: {connectionId}");
            };

            // Register status update listener
            _hubConnection.On<string>("StatusUpdate", async (statusJson) =>
            {
                // Debug logging for SignalR status updates
                await LogToConsoleAsync($"[SignalR] StatusUpdate received: {statusJson}");
                
                var status = DeserializeStatusInformation(statusJson);
                if (status != null)
                {
                    var statusType = status switch
                    {
                        Models.Chat.StatusTypes.AssessmentGeneratingStatus => "assessment-generating",
                        Models.Chat.StatusTypes.AssessmentCompleteStatus => "assessment-complete",
                        Models.Chat.StatusTypes.AssessmentCreatedStatus => "assessment-created",
                        Models.Chat.StatusTypes.AssessmentAnalyzingStatus => "assessment-analyzing",
                        _ => "unknown"
                    };
                    await LogToConsoleAsync($"[SignalR] Deserialized status type: {statusType}, Timestamp: {status.Timestamp:HH:mm:ss.fff}");
                    
                    if (StatusUpdateReceived != null)
                    {
                        await LogToConsoleAsync($"[SignalR] Invoking StatusUpdateReceived handler for: {statusType}");
                        await StatusUpdateReceived(status);
                        await LogToConsoleAsync($"[SignalR] StatusUpdateReceived handler completed for: {statusType}");
                    }
                }
                else
                {
                    await LogToConsoleAsync($"[SignalR] Failed to deserialize status update");
                }
            });
        }

        if (_hubConnection.State == HubConnectionState.Disconnected)
        {
            await LogToConsoleAsync("[SignalR] Starting connection...");
            await _hubConnection.StartAsync();
            await LogToConsoleAsync($"[SignalR] Connection started. State: {_hubConnection.State}");
        }
    }

    public async Task<HealthChatResponse> SendMessageAsync(string message, Guid? conversationId)
    {
        if (_hubConnection == null || _hubConnection.State != HubConnectionState.Connected)
        {
            throw new InvalidOperationException("Chat connection is not established.");
        }

        await LogToConsoleAsync($"[SignalR] Sending message. ConversationId: {conversationId}");
        var response = await _hubConnection.InvokeAsync<HealthChatResponse>("SendMessage", message, conversationId);
        await LogToConsoleAsync($"[SignalR] Message response received. ConversationId: {response.ConversationId}");
        return response;
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
    
    private static StatusInformation? DeserializeStatusInformation(string statusJson)
    {
        try
        {
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var element = JsonSerializer.Deserialize<JsonElement>(statusJson, jsonOptions);
            if (!element.TryGetProperty("type", out var typeElement))
            {
                return null;
            }

            var type = typeElement.GetString();
            var timestamp = element.TryGetProperty("timestamp", out var timestampElement)
                ? timestampElement.GetDateTime()
                : DateTime.UtcNow;

            return type switch
            {
                "assessment-generating" => new Models.Chat.StatusTypes.AssessmentGeneratingStatus
                {
                    Message = element.TryGetProperty("message", out var msg) ? msg.GetString() ?? "Generating assessment..." : "Generating assessment...",
                    Timestamp = timestamp
                },
                "assessment-complete" => new Models.Chat.StatusTypes.AssessmentCompleteStatus
                {
                    Message = element.TryGetProperty("message", out var msg) ? msg.GetString() ?? "Assessment complete" : "Assessment complete",
                    Timestamp = timestamp
                },
                "assessment-analyzing" => new Models.Chat.StatusTypes.AssessmentAnalyzingStatus
                {
                    Message = element.TryGetProperty("message", out var msg) ? msg.GetString() ?? "Analyzing assessment..." : "Analyzing assessment...",
                    Timestamp = timestamp
                },
                "assessment-created" => new Models.Chat.StatusTypes.AssessmentCreatedStatus
                {
                    AssessmentId = element.TryGetProperty("assessmentId", out var id) ? id.GetInt32() : 0,
                    Hypothesis = element.TryGetProperty("hypothesis", out var hyp) ? hyp.GetString() ?? "Assessment" : "Assessment",
                    Confidence = element.TryGetProperty("confidence", out var conf) ? conf.GetDecimal() : 0m,
                    Timestamp = timestamp
                },
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
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

