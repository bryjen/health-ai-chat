using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Web.Common.DTOs.Health;
using WebFrontend.Models.Chat;
using WebApi.ApiWrapper.Services;

namespace WebFrontend.Services;

/// <summary>
/// Concrete client for the health chat SignalR hub.
/// Manages the hub connection and sending messages.
/// </summary>
public class ChatHubClient(ITokenProvider tokenProvider)
    : IAsyncDisposable
{
    private HubConnection? _hubConnection;

    // TODO: consider moving this to configuration if needed
    private const string HubUrl = "https://localhost:7265/hubs/chat";

    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    public event Func<StatusInformation, Task>? StatusUpdateReceived;

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
                    options.AccessTokenProvider = async () => await tokenProvider.GetTokenAsync();
                })
                .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
                .Build();

            // Register status update listener
            _hubConnection.On<string>("StatusUpdate", async (statusJson) =>
            {
                var status = DeserializeStatusInformation(statusJson);
                if (status != null && StatusUpdateReceived != null)
                {
                    await StatusUpdateReceived(status);
                }
            });
        }

        // Handle different connection states
        var currentState = _hubConnection.State;

        if (currentState == HubConnectionState.Disconnected)
        {
            try
            {
                await _hubConnection.StartAsync();
            }
            catch (Exception)
            {
                throw;
            }
        }
        else if (currentState == HubConnectionState.Reconnecting)
        {
            // Wait for automatic reconnect to complete (with timeout)
            var timeout = TimeSpan.FromSeconds(30);
            var startTime = DateTime.UtcNow;
            while (_hubConnection.State == HubConnectionState.Reconnecting && DateTime.UtcNow - startTime < timeout)
            {
                await Task.Delay(500);
            }

            // If still reconnecting after timeout, force stop and restart
            if (_hubConnection.State == HubConnectionState.Reconnecting)
            {
                try
                {
                    await _hubConnection.StopAsync();
                }
                catch
                {
                    // Ignore errors when stopping
                }

                // Wait a bit then start fresh
                await Task.Delay(1000);
                await _hubConnection.StartAsync();
            }
        }
    }

    public async Task<HealthChatResponse> SendMessageAsync(string message, Guid? conversationId)
    {
        if (_hubConnection == null || _hubConnection.State != HubConnectionState.Connected)
        {
            throw new InvalidOperationException("Chat connection is not established.");
        }

        var response = await _hubConnection.InvokeAsync<HealthChatResponse>("ProcessMessage", message, conversationId);
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

