using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Threading;
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
    private ChatMessage? _currentProcessingMessage;
    private readonly List<StatusInformation> _currentStatusUpdates = new();
    private readonly ConcurrentQueue<StatusInformation> _statusUpdateQueue = new();
    private CancellationTokenSource? _renderLoopCancellation;
    private Task? _renderLoopTask;
    private SynchronizationContext? _syncContext;

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
            // Connect SignalR first so IsConnected is true quickly
            await ChatHubClient.ConnectAsync();
            
            // Subscribe to status updates
            ChatHubClient.StatusUpdateReceived += OnStatusUpdateReceived;
            
            // Start debounced render loop
            StartRenderLoop();

            // Load conversation after connection is established
            await HandleConversationParameterChange();
        }
        catch
        {
            // Handle connection error - could show error message to user
            // Still try to load conversation even if SignalR fails
            await HandleConversationParameterChange();
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        // Handle conversation parameter changes (when clicking different conversations)
        // Always process parameter changes, even if SignalR isn't connected yet
        // We can still load conversation data from the API
        await HandleConversationParameterChange();
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

        // Always reload if it's a different conversation, or if we don't have messages loaded
        // This ensures navigation back works correctly
        if (conversationId != _lastLoadedConversationId || Messages.Count == 0)
        {
            await LoadConversationAsync(conversationId);
        }
    }

    private async Task LoadConversationAsync(Guid conversationId)
    {
        try
        {
            IsLoading = true;
            await InvokeAsync(StateHasChanged);

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
            await InvokeAsync(StateHasChanged);
            // Scrolling handled by ChatMessageList component after render
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Scrolling is now handled by ChatMessageList component
        await Task.CompletedTask;
    }
    
    private void StartRenderLoop()
    {
        // Capture synchronization context for UI thread access
        _syncContext = SynchronizationContext.Current;
        _renderLoopCancellation = new CancellationTokenSource();
        _renderLoopTask = Task.Run(async () => await RenderLoopAsync(_renderLoopCancellation.Token));
    }
    
    private async Task RenderLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var processedAny = false;
                var processedCount = 0;
                
                // Process all queued status updates
                while (_statusUpdateQueue.TryDequeue(out var status))
                {
                    processedAny = true;
                    processedCount++;
                    await ProcessStatusUpdateAsync(status);
                }
                
                // If we processed any updates, trigger a render
                if (processedAny)
                {
                    // Post to UI thread via sync context
                    if (_syncContext != null)
                    {
                        var tcs = new TaskCompletionSource();
                        _syncContext.Post(_ =>
                        {
                            _ = InvokeAsync(() =>
                            {
                                StateHasChanged();
                                return Task.CompletedTask;
                            });
                            tcs.SetResult();
                        }, null);
                        await tcs.Task;
                    }
                    else
                    {
                        // Fallback: try InvokeAsync directly
                        await InvokeAsync(StateHasChanged);
                    }
                }
                
                // Debounce: wait 50ms before next check (max 20fps)
                await Task.Delay(50, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // Continue loop even on error
            }
        }
    }
    
    private async Task ProcessStatusUpdateAsync(StatusInformation status)
    {
        if (_currentProcessingMessage != null)
        {
            _currentStatusUpdates.Add(status);
            
            // Sort status updates by type order to maintain correct display order
            var sortedStatuses = _currentStatusUpdates
                .OrderBy(s => GetStatusSortOrder(s))
                .ThenBy(s => s.Timestamp)
                .ToList();
            
            // Always create new list instance to ensure Blazor detects change
            _currentProcessingMessage.StatusInformation = new List<StatusInformation>(sortedStatuses);
            
            // Only add the message to UI if it hasn't been added yet and we have status updates
            // This prevents showing empty bubbles
            if (_currentProcessingMessage.Content == null && _currentStatusUpdates.Any())
            {
                _currentProcessingMessage.Content = string.Empty; // Mark as added but keep content empty
                Messages.Add(_currentProcessingMessage);
            }
        }
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

        // Create a temporary message for status updates during processing
        // Don't add it to Messages yet - only add when we have content or status updates
        _currentProcessingMessage = new ChatMessage
        {
            Content = null, // null means not yet added to UI
            IsUser = false,
            Timestamp = DateTime.Now,
            StatusInformation = new List<StatusInformation>()
        };
        _currentStatusUpdates.Clear();
        // Clear any pending status updates from previous messages
        while (_statusUpdateQueue.TryDequeue(out _)) { }

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

            // Merge real-time status updates with EntityChanges-based statuses
            var entityStatuses = ConvertEntityChangesToStatusInformation(
                response.SymptomChanges, 
                response.AssessmentChanges);
            
            // Combine real-time statuses with entity-based statuses
            var allStatuses = new List<StatusInformation>(_currentStatusUpdates);
            
            // Add entity-based statuses, avoiding duplicates
            foreach (var entityStatus in entityStatuses)
            {
                // Avoid duplicates - if we already have an assessment-created status, don't add another
                if (entityStatus is AssessmentCreatedStatus createdStatus)
                {
                    var existingCreated = allStatuses.OfType<AssessmentCreatedStatus>()
                        .FirstOrDefault(s => s.AssessmentId == createdStatus.AssessmentId);
                    if (existingCreated == null)
                    {
                        allStatuses.Add(entityStatus);
                    }
                }
                else
                {
                    allStatuses.Add(entityStatus);
                }
            }
            
            // Sort status messages by timestamp to maintain correct order
            // Order should be: assessment-generating -> assessment-created -> assessment-analyzing -> others
            allStatuses = allStatuses.OrderBy(s => GetStatusSortOrder(s)).ThenBy(s => s.Timestamp).ToList();

            // Remove processing message if it was added
            if (_currentProcessingMessage != null && Messages.Contains(_currentProcessingMessage))
            {
                Messages.Remove(_currentProcessingMessage);
            }

            // Create final AI message with all statuses
            var aiMessage = new ChatMessage
            {
                Content = response.Message,
                IsUser = false,
                Timestamp = DateTime.Now,
                StatusInformation = new List<StatusInformation>(allStatuses) // New list instance
            };

            Messages.Add(aiMessage);
            
            _currentProcessingMessage = null;
            _currentStatusUpdates.Clear();
        }
        catch (Exception ex)
        {
            // Remove temporary processing message if it was added
            if (_currentProcessingMessage != null && _currentProcessingMessage.Content != null)
            {
                Messages.Remove(_currentProcessingMessage);
            }

            var errorMessage = new ChatMessage
            {
                Content = $"Error: Failed to send message. {ex.Message}",
                IsUser = false,
                Timestamp = DateTime.Now
            };
            Messages.Add(errorMessage);
            _currentProcessingMessage = null;
            _currentStatusUpdates.Clear();
        }
        finally
        {
            IsLoading = false;
            StateHasChanged();
            // Scrolling handled by ChatMessageList component after render
        }
    }

    private async Task OnStatusUpdateReceived(StatusInformation status)
    {
        // SignalR handler - only enqueues, never triggers render directly
        _statusUpdateQueue.Enqueue(status);
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

    private static int GetStatusSortOrder(StatusInformation status)
    {
        // Order: assessment-generating (1) -> assessment-created (2) -> assessment-analyzing (3) -> others (4)
        return status switch
        {
            AssessmentGeneratingStatus => 1,
            AssessmentCreatedStatus => 2,
            AssessmentAnalyzingStatus => 3,
            _ => 5
        };
    }

    private static List<StatusInformation> ConvertEntityChangesToStatusInformation(
        List<EntityChange>? entityChanges,
        List<EntityChange>? assessmentChanges = null)
    {
        if (entityChanges == null || !entityChanges.Any())
        {
            return new List<StatusInformation>();
        }

        var statusList = new List<StatusInformation>();

        if (entityChanges != null)
        {
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
        }

        // Add assessment changes
        if (assessmentChanges != null)
        {
            foreach (var change in assessmentChanges)
            {
                if (change.Action.ToLowerInvariant() == "created" && 
                    int.TryParse(change.Id, out var assessmentId))
                {
                    statusList.Add(new AssessmentCreatedStatus
                    {
                        AssessmentId = assessmentId,
                        Hypothesis = change.Name ?? "Assessment",
                        Confidence = change.Confidence ?? 0m
                    });
                }
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

                    case "assessment-generating":
                        if (element.TryGetProperty("message", out var generatingMessageElement))
                        {
                            statusList.Add(new AssessmentGeneratingStatus
                            {
                                Message = generatingMessageElement.GetString() ?? "Generating assessment...",
                                Timestamp = timestamp
                            });
                        }
                        break;

                    case "assessment-analyzing":
                        if (element.TryGetProperty("message", out var analyzingMessageElement))
                        {
                            statusList.Add(new AssessmentAnalyzingStatus
                            {
                                Message = analyzingMessageElement.GetString() ?? "Analyzing assessment...",
                                Timestamp = timestamp
                            });
                        }
                        break;

                    case "assessment-created":
                        if (element.TryGetProperty("assessmentId", out var assessmentIdElement) &&
                            element.TryGetProperty("hypothesis", out var hypothesisElement) &&
                            element.TryGetProperty("confidence", out var confidenceElement))
                        {
                            statusList.Add(new AssessmentCreatedStatus
                            {
                                AssessmentId = assessmentIdElement.GetInt32(),
                                Hypothesis = hypothesisElement.GetString() ?? "Assessment",
                                Confidence = confidenceElement.GetDecimal(),
                                Timestamp = timestamp
                            });
                        }
                        break;
                }
            }

            // Sort status messages by type order to maintain correct display order
            // Order: assessment-generating -> assessment-created -> assessment-analyzing -> others
            return statusList
                .OrderBy(s => GetStatusSortOrder(s))
                .ThenBy(s => s.Timestamp)
                .ToList();
        }
        catch (JsonException)
        {
            // If deserialization fails, return empty list
            return new List<StatusInformation>();
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Stop render loop
        if (_renderLoopCancellation != null)
        {
            _renderLoopCancellation.Cancel();
            _renderLoopCancellation.Dispose();
        }
        
        // Wait for render loop to finish
        if (_renderLoopTask != null)
        {
            try
            {
                await _renderLoopTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelling
            }
        }
        
        // Unsubscribe from status updates
        ChatHubClient.StatusUpdateReceived -= OnStatusUpdateReceived;
        
        // Don't dispose ChatHubClient here - it's a scoped service managed by DI
        // Disposing it here would break reconnection when navigating back
    }
}
