using System;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Web.Common.DTOs.Health;
using WebApi.ApiWrapper.Services;
using WebFrontend.Components.UI.Select;
using WebFrontend.Models.Chat;
using WebFrontend.Models.Chat.StatusTypes;
using WebFrontend.Services;
using WebFrontend.Services.Auth;

namespace WebFrontend.Pages;

public partial class Chat : ComponentBase, IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private AuthenticationStateProvider AuthStateProvider { get; set; } = default!;
    [Inject] private IConversationsApiClient ConversationsApiClient { get; set; } = default!;
    [Inject] private AuthService AuthService { get; set; } = default!;
    [Inject] private ChatHubClient ChatHubClient { get; set; } = default!;

    [Parameter]
    [SupplyParameterFromQuery]
    public string? Conversation { get; set; }

    protected List<ChatMessage> Messages { get; set; } = new();
    protected string InputText { get; set; } = string.Empty;
    protected bool IsLoading { get; set; } = false;
    protected bool IsConnected => ChatHubClient.IsConnected;
    private Guid? _currentConversationId;
    private Guid? _lastLoadedConversationId;

    protected override async Task OnInitializedAsync()
    {
        // Check authentication
        var authState = await AuthStateProvider.GetAuthenticationStateAsync();
        if (!authState.User.Identity?.IsAuthenticated ?? true)
        {
            Navigation.NavigateTo("/login");
            return;
        }

        // Load current user for greeting
        _ = AuthService.GetCurrentUserAsync();

        try
        {
            await ChatHubClient.ConnectAsync();

            // Load conversation after connection is established
            await HandleConversationParameterChange();
        }
        catch (Exception ex)
        {
            // Handle connection error - could show error message to user
            Console.WriteLine($"SignalR connection error: {ex.Message}");
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        // Handle conversation parameter changes (when clicking different conversations)
        // Only process if SignalR is connected
        if (ChatHubClient.IsConnected)
        {
            await HandleConversationParameterChange();
        }
    }

    private async Task HandleConversationParameterChange()
    {
        // Clear messages if no conversation parameter
        if (string.IsNullOrWhiteSpace(Conversation))
        {
            if (_currentConversationId != null)
            {
                // Starting a new chat - clear everything
                _currentConversationId = null;
                _lastLoadedConversationId = null;
                Messages.Clear();
                StateHasChanged();
            }
            return;
        }

        // Parse conversation ID
        if (!Guid.TryParse(Conversation, out var conversationId))
        {
            return;
        }

        // Only load if it's a different conversation than what's currently loaded
        if (conversationId != _lastLoadedConversationId)
        {
            await LoadConversationAsync(conversationId);
        }
    }

    private async Task LoadConversationAsync(Guid conversationId)
    {
        try
        {
            IsLoading = true;
            StateHasChanged();

            var conversation = await ConversationsApiClient.GetConversationByIdAsync(conversationId);

            if (conversation != null)
            {
                _currentConversationId = conversation.Id;
                _lastLoadedConversationId = conversation.Id;
                Messages = conversation.Messages
                    .OrderBy(m => m.CreatedAt)
                    .ThenBy(m => GetRoleSortOrder(m.Role))
                    .ThenBy(m => m.Id)
                    .Select(m => new ChatMessage
                    {
                        Content = m.Content,
                        IsUser = m.Role.ToLowerInvariant() == "user",
                        Timestamp = m.CreatedAt,
                        StatusInformation = DeserializeStatusInformation(m.StatusInformationJson)
                    })
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading conversation: {ex.Message}");
            // Show error to user
            Messages.Add(new ChatMessage
            {
                Content = $"Error loading conversation: {ex.Message}",
                IsUser = false,
                Timestamp = DateTime.Now
            });
        }
        finally
        {
            IsLoading = false;
            StateHasChanged();
            // Scrolling handled by ChatMessageList component after render
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Scrolling is now handled by ChatMessageList component
        await Task.CompletedTask;
    }

    protected async Task HandleSubmit()
    {
        if (string.IsNullOrWhiteSpace(InputText) || IsLoading || !ChatHubClient.IsConnected)
        {
            return;
        }

        var userMessage = new ChatMessage
        {
            Content = InputText.Trim(),
            IsUser = true,
            Timestamp = DateTime.Now
        };

            Messages.Add(userMessage);
        var currentInput = InputText;
        InputText = string.Empty;
        IsLoading = true;

        StateHasChanged();
        // Scrolling handled by ChatMessageList component

        try
        {
            var response = await ChatHubClient.SendMessageAsync(currentInput, _currentConversationId);

            var wasNewConversation = _currentConversationId == null;
            _currentConversationId = response.ConversationId;

            if (wasNewConversation)
            {
                Navigation.NavigateTo($"/chat?conversation={_currentConversationId}", false);
            }

            var aiMessage = new ChatMessage
            {
                Content = response.Message,
                IsUser = false,
                Timestamp = DateTime.Now,
                StatusInformation = ConvertEntityChangesToStatusInformation(response.SymptomChanges)
            };

            Messages.Add(aiMessage);
        }
        catch (Exception ex)
        {
            var errorMessage = new ChatMessage
            {
                Content = $"Error: Failed to send message. {ex.Message}",
                IsUser = false,
                Timestamp = DateTime.Now
            };
            Messages.Add(errorMessage);
        }
        finally
        {
            IsLoading = false;
            StateHasChanged();
            // Scrolling handled by ChatMessageList component after render
        }
    }

    protected void OnInputTextChanged(string value)
    {
        InputText = value;
    }

    private async Task ScrollToBottom()
    {
        // ChatMessageList component handles scrolling automatically via OnAfterRenderAsync
        // This method is kept for compatibility but scrolling is now handled by the component
        await Task.CompletedTask;
    }

    protected string GetGreeting()
    {
        var hour = DateTime.Now.Hour;
        var user = AuthService.CurrentUser;
        var name = user?.Email?.Split('@')[0] ?? "User";

        // Capitalize first letter
        if (!string.IsNullOrEmpty(name) && name.Length > 0)
        {
            name = char.ToUpper(name[0]) + (name.Length > 1 ? name.Substring(1) : "");
        }

        // TEMP, TODO REMOVE WHEN WE HAVE USERS AND STUFF LIKE THAT
        name = "John";

        return hour switch
        {
            >= 5 and < 12 => $"Good morning, {name}",
            >= 12 and < 17 => $"Good afternoon, {name}",
            _ => $"Evening, {name}"
        };
    }

    private static int GetRoleSortOrder(string role)
    {
        if (string.Equals(role, "user", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return 2;
    }

    private static List<StatusInformation> ConvertEntityChangesToStatusInformation(List<EntityChange>? entityChanges)
    {
        if (entityChanges == null || !entityChanges.Any())
        {
            return new List<StatusInformation>();
        }

        var statusList = new List<StatusInformation>();

        foreach (var change in entityChanges)
        {
            switch (change.Action.ToLowerInvariant())
            {
                case "created":
                    if (int.TryParse(change.Id, out var episodeId))
                    {
                        statusList.Add(new SymptomAddedStatus
                        {
                            SymptomName = change.Name ?? "Unknown symptom",
                            EpisodeId = episodeId,
                            Location = null // Could be fetched from API if needed
                        });
                    }
                    break;

                case "updated":
                    statusList.Add(new GeneralStatus
                    {
                        Message = !string.IsNullOrEmpty(change.Name) 
                            ? $"Updated {change.Name} details"
                            : "Updated symptom details"
                    });
                    break;

                case "resolved":
                    statusList.Add(new GeneralStatus
                    {
                        Message = !string.IsNullOrEmpty(change.Name)
                            ? $"Resolved {change.Name}"
                            : "Resolved symptom"
                    });
                    break;
            }
        }

        return statusList;
    }

    private static List<StatusInformation> DeserializeStatusInformation(string? statusInformationJson)
    {
        if (string.IsNullOrWhiteSpace(statusInformationJson))
        {
            return new List<StatusInformation>();
        }

        try
        {
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var jsonArray = JsonSerializer.Deserialize<JsonElement[]>(statusInformationJson, jsonOptions);
            if (jsonArray == null)
            {
                return new List<StatusInformation>();
            }

            var statusList = new List<StatusInformation>();

            foreach (var element in jsonArray)
            {
                if (!element.TryGetProperty("type", out var typeElement))
                {
                    continue;
                }

                var type = typeElement.GetString();
                var timestamp = element.TryGetProperty("timestamp", out var timestampElement)
                    ? timestampElement.GetDateTime()
                    : DateTime.UtcNow;

                switch (type)
                {
                    case "symptom-added":
                        if (element.TryGetProperty("symptomName", out var symptomNameElement) &&
                            element.TryGetProperty("episodeId", out var episodeIdElement))
                        {
                            var location = element.TryGetProperty("location", out var locationElement) && locationElement.ValueKind != JsonValueKind.Null
                                ? locationElement.GetString()
                                : null;

                            statusList.Add(new SymptomAddedStatus
                            {
                                SymptomName = symptomNameElement.GetString() ?? "Unknown symptom",
                                EpisodeId = episodeIdElement.GetInt32(),
                                Location = location,
                                Timestamp = timestamp
                            });
                        }
                        break;

                    case "general":
                        if (element.TryGetProperty("message", out var messageElement))
                        {
                            statusList.Add(new GeneralStatus
                            {
                                Message = messageElement.GetString() ?? "Status update",
                                Timestamp = timestamp
                            });
                        }
                        break;

                    case "symptom-gathering":
                        if (element.TryGetProperty("message", out var gatheringMessageElement))
                        {
                            statusList.Add(new SymptomGatheringStatus
                            {
                                Message = gatheringMessageElement.GetString() ?? "Gathering symptom details",
                                Timestamp = timestamp
                            });
                        }
                        break;
                }
            }

            return statusList;
        }
        catch (JsonException)
        {
            // If deserialization fails, return empty list
            return new List<StatusInformation>();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ChatHubClient.DisposeAsync();
    }
}
