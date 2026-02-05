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
                        Content = ExtractMessageFromJson(m.Content),
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
                    var statusType = status.GetType().Name;
                    var statusInfo = status switch
                    {
                        AssessmentGeneratingStatus gen => $"Generating: {gen.Message}",
                        AssessmentAnalyzingStatus anal => $"Analyzing: {anal.Message}",
                        AssessmentCreatedStatus created => $"Created: ID={created.AssessmentId}, Hypothesis={created.Hypothesis}",
                        GeneralStatus gen => $"General: {gen.Message}",
                        _ => status.ToString() ?? "Unknown"
                    };
                    await JS.InvokeVoidAsync("console.log", $"[RENDER LOOP] Dequeued status: Type={statusType}, Info={statusInfo}, QueueSize: {_statusUpdateQueue.Count}");
                    await ProcessStatusUpdateAsync(status);
                }
                
                if (processedCount > 0)
                {
                    await JS.InvokeVoidAsync("console.log", $"[RENDER LOOP] Processed {processedCount} status updates, QueueSize: {_statusUpdateQueue.Count}");
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
        var statusType = status.GetType().Name;
        var statusInfo = status switch
        {
            AssessmentGeneratingStatus gen => $"Generating: {gen.Message}",
            AssessmentAnalyzingStatus anal => $"Analyzing: {anal.Message}",
            AssessmentCreatedStatus created => $"Created: ID={created.AssessmentId}, Hypothesis={created.Hypothesis}",
            GeneralStatus gen => $"General: {gen.Message}",
            _ => status.ToString() ?? "Unknown"
        };
        
        await JS.InvokeVoidAsync("console.log", $"[PROCESS STATUS] Processing: Type={statusType}, Info={statusInfo}, HasProcessingMessage={_currentProcessingMessage != null}, CurrentStatusCount={_currentStatusUpdates.Count}");
        
        if (_currentProcessingMessage != null)
        {
            // Check for duplicates before adding
            bool isDuplicate = false;
            
            if (status is AssessmentGeneratingStatus)
            {
                isDuplicate = _currentStatusUpdates.OfType<AssessmentGeneratingStatus>().Any();
                await JS.InvokeVoidAsync("console.log", $"[PROCESS STATUS] AssessmentGeneratingStatus duplicate check: {isDuplicate}");
            }
            else if (status is AssessmentAnalyzingStatus)
            {
                isDuplicate = _currentStatusUpdates.OfType<AssessmentAnalyzingStatus>().Any();
                await JS.InvokeVoidAsync("console.log", $"[PROCESS STATUS] AssessmentAnalyzingStatus duplicate check: {isDuplicate}");
            }
            else if (status is AssessmentCreatedStatus created)
            {
                isDuplicate = _currentStatusUpdates.OfType<AssessmentCreatedStatus>()
                    .Any(s => s.AssessmentId == created.AssessmentId);
                await JS.InvokeVoidAsync("console.log", $"[PROCESS STATUS] AssessmentCreatedStatus duplicate check: {isDuplicate} (ID={created.AssessmentId})");
            }
            else if (status is GeneralStatus general)
            {
                // Check for duplicate general statuses with the same message
                isDuplicate = _currentStatusUpdates.OfType<GeneralStatus>()
                    .Any(s => s.Message == general.Message);
                await JS.InvokeVoidAsync("console.log", $"[PROCESS STATUS] GeneralStatus duplicate check: {isDuplicate} (Message={general.Message})");
            }
            
            if (!isDuplicate)
            {
                _currentStatusUpdates.Add(status);
                await JS.InvokeVoidAsync("console.log", $"[PROCESS STATUS] Added to _currentStatusUpdates. New count: {_currentStatusUpdates.Count}");
            }
            else
            {
                await JS.InvokeVoidAsync("console.log", $"[PROCESS STATUS] DUPLICATE DETECTED - Not adding. Current count: {_currentStatusUpdates.Count}");
            }
            
            // Deduplicate and sort status updates by type order
            var deduplicatedStatuses = new List<StatusInformation>();
            var seenGenerating = false;
            var seenAnalyzing = false;
            var seenCreatedIds = new HashSet<int>();
            var seenGeneralMessages = new HashSet<string>();
            
            foreach (var s in _currentStatusUpdates.OrderBy(s => GetStatusSortOrder(s)).ThenBy(s => s.Timestamp))
            {
                if (s is AssessmentGeneratingStatus)
                {
                    if (!seenGenerating)
                    {
                        deduplicatedStatuses.Add(s);
                        seenGenerating = true;
                    }
                }
                else if (s is AssessmentAnalyzingStatus)
                {
                    if (!seenAnalyzing)
                    {
                        deduplicatedStatuses.Add(s);
                        seenAnalyzing = true;
                    }
                }
                else if (s is AssessmentCreatedStatus created)
                {
                    if (!seenCreatedIds.Contains(created.AssessmentId))
                    {
                        deduplicatedStatuses.Add(s);
                        seenCreatedIds.Add(created.AssessmentId);
                    }
                }
                else if (s is GeneralStatus general)
                {
                    // Deduplicate general statuses with the same message
                    var messageKey = general.Message ?? string.Empty;
                    if (!seenGeneralMessages.Contains(messageKey))
                    {
                        deduplicatedStatuses.Add(s);
                        seenGeneralMessages.Add(messageKey);
                    }
                }
                else
                {
                    deduplicatedStatuses.Add(s);
                }
            }
            
            // Always create new list instance to ensure Blazor detects change
            _currentProcessingMessage.StatusInformation = deduplicatedStatuses;
            
            var statusTypes = string.Join(", ", deduplicatedStatuses.Select(s => s.GetType().Name));
            await JS.InvokeVoidAsync("console.log", $"[PROCESS STATUS] After deduplication: Count={deduplicatedStatuses.Count}, Types=[{statusTypes}]");
            
            // Only add the message to UI if it hasn't been added yet and we have status updates
            // This prevents showing empty bubbles
            if (_currentProcessingMessage.Content == null && deduplicatedStatuses.Any())
            {
                _currentProcessingMessage.Content = string.Empty; // Mark as added but keep content empty
                Messages.Add(_currentProcessingMessage);
                await JS.InvokeVoidAsync("console.log", $"[PROCESS STATUS] Added processing message to UI. Total messages: {Messages.Count}");
            }
        }
        else
        {
            await JS.InvokeVoidAsync("console.log", $"[PROCESS STATUS] WARNING: No _currentProcessingMessage! Status will be lost.");
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
                // Avoid duplicates for assessment statuses - each type should only appear once
                if (entityStatus is AssessmentCreatedStatus createdStatus)
                {
                    var existingCreated = allStatuses.OfType<AssessmentCreatedStatus>()
                        .FirstOrDefault(s => s.AssessmentId == createdStatus.AssessmentId);
                    if (existingCreated == null)
                    {
                        allStatuses.Add(entityStatus);
                    }
                }
                else if (entityStatus is AssessmentGeneratingStatus)
                {
                    // Only one generating status per message
                    var existingGenerating = allStatuses.OfType<AssessmentGeneratingStatus>().FirstOrDefault();
                    if (existingGenerating == null)
                    {
                        allStatuses.Add(entityStatus);
                    }
                }
                else if (entityStatus is AssessmentAnalyzingStatus)
                {
                    // Only one analyzing status per message
                    var existingAnalyzing = allStatuses.OfType<AssessmentAnalyzingStatus>().FirstOrDefault();
                    if (existingAnalyzing == null)
                    {
                        allStatuses.Add(entityStatus);
                    }
                }
                else
                {
                    allStatuses.Add(entityStatus);
                }
            }
            
            // Deduplicate by type - ensure each assessment status type appears only once
            // Also deduplicate general statuses with the same message
            var deduplicatedStatuses = new List<StatusInformation>();
            var seenGenerating = false;
            var seenAnalyzing = false;
            var seenCreatedIds = new HashSet<int>();
            var seenGeneralMessages = new HashSet<string>();
            
            foreach (var status in allStatuses.OrderBy(s => GetStatusSortOrder(s)).ThenBy(s => s.Timestamp))
            {
                if (status is AssessmentGeneratingStatus)
                {
                    if (!seenGenerating)
                    {
                        deduplicatedStatuses.Add(status);
                        seenGenerating = true;
                    }
                }
                else if (status is AssessmentAnalyzingStatus)
                {
                    if (!seenAnalyzing)
                    {
                        deduplicatedStatuses.Add(status);
                        seenAnalyzing = true;
                    }
                }
                else if (status is AssessmentCreatedStatus created)
                {
                    if (!seenCreatedIds.Contains(created.AssessmentId))
                    {
                        deduplicatedStatuses.Add(status);
                        seenCreatedIds.Add(created.AssessmentId);
                    }
                }
                else if (status is GeneralStatus general)
                {
                    // Deduplicate general statuses with the same message
                    var messageKey = general.Message ?? string.Empty;
                    if (!seenGeneralMessages.Contains(messageKey))
                    {
                        deduplicatedStatuses.Add(status);
                        seenGeneralMessages.Add(messageKey);
                    }
                }
                else
                {
                    deduplicatedStatuses.Add(status);
                }
            }
            
            allStatuses = deduplicatedStatuses;

            // Remove processing message if it was added
            if (_currentProcessingMessage != null && Messages.Contains(_currentProcessingMessage))
            {
                Messages.Remove(_currentProcessingMessage);
            }

            // Extract message from JSON if content is JSON
            var messageContent = ExtractMessageFromJson(response.Message);
            
            // Create final AI message with all statuses
            var aiMessage = new ChatMessage
            {
                Content = messageContent,
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
        var statusType = status.GetType().Name;
        var statusInfo = status switch
        {
            AssessmentGeneratingStatus gen => $"Generating: {gen.Message}",
            AssessmentAnalyzingStatus anal => $"Analyzing: {anal.Message}",
            AssessmentCreatedStatus created => $"Created: ID={created.AssessmentId}, Hypothesis={created.Hypothesis}",
            GeneralStatus gen => $"General: {gen.Message}",
            _ => status.ToString() ?? "Unknown"
        };
        await JS.InvokeVoidAsync("console.log", $"[STATUS ENQUEUE] Type: {statusType}, Info: {statusInfo}, QueueSize: {_statusUpdateQueue.Count}");
        _statusUpdateQueue.Enqueue(status);
        await JS.InvokeVoidAsync("console.log", $"[STATUS ENQUEUE] After enqueue, QueueSize: {_statusUpdateQueue.Count}");
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

    private static string ExtractMessageFromJson(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return content;
        }

        // Check if content looks like JSON
        var trimmed = content.Trim();
        if (!trimmed.StartsWith("{") || (!trimmed.Contains("\"message\"") && !trimmed.Contains("\"response\"")))
        {
            return content; // Not JSON, return as-is
        }

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            // Try "message" field first (most common)
            if (root.TryGetProperty("message", out var messageElement))
            {
                // Handle both string and object types
                if (messageElement.ValueKind == JsonValueKind.String)
                {
                    var message = messageElement.GetString();
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        return message;
                    }
                }
                else if (messageElement.ValueKind == JsonValueKind.Object)
                {
                    // If message is an object (like assessment JSON), serialize it back to JSON
                    return messageElement.GetRawText();
                }
            }

            // Try "response" field (alternative)
            if (root.TryGetProperty("response", out var responseElement))
            {
                // Handle both string and object types
                if (responseElement.ValueKind == JsonValueKind.String)
                {
                    var response = responseElement.GetString();
                    if (!string.IsNullOrWhiteSpace(response))
                    {
                        return response;
                    }
                }
                else if (responseElement.ValueKind == JsonValueKind.Object)
                {
                    // If response is an object, serialize it back to JSON
                    return responseElement.GetRawText();
                }
            }

            // If JSON but no message/response field, try to generate a readable message from the JSON structure
            var messageParts = new List<string>();

            // Extract symptoms if present
            if (root.TryGetProperty("symptoms", out var symptomsElement) && symptomsElement.ValueKind == JsonValueKind.Array)
            {
                var symptomList = new List<string>();
                foreach (var symptom in symptomsElement.EnumerateArray())
                {
                    if (symptom.ValueKind == JsonValueKind.Object)
                    {
                        if (symptom.TryGetProperty("name", out var nameElement))
                        {
                            symptomList.Add(nameElement.GetString() ?? "symptom");
                        }
                    }
                    else if (symptom.ValueKind == JsonValueKind.String)
                    {
                        symptomList.Add(symptom.GetString() ?? "symptom");
                    }
                }
                if (symptomList.Any())
                {
                    messageParts.Add($"I've noted your symptoms: {string.Join(", ", symptomList)}.");
                }
            }

            // Extract questions if present
            if (root.TryGetProperty("questions", out var questionsElement) && questionsElement.ValueKind == JsonValueKind.Array)
            {
                var questionList = new List<string>();
                foreach (var question in questionsElement.EnumerateArray())
                {
                    if (question.ValueKind == JsonValueKind.String)
                    {
                        questionList.Add(question.GetString() ?? "");
                    }
                }
                if (questionList.Any())
                {
                    messageParts.Add(string.Join(" ", questionList));
                }
            }

            // If we generated any message parts, use them; otherwise return formatted JSON
            if (messageParts.Any())
            {
                return string.Join("\n\n", messageParts);
            }

            // Last resort: return formatted JSON (pretty-printed)
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                return JsonSerializer.Serialize(root, options);
            }
            catch
            {
                return content;
            }
        }
        catch (JsonException)
        {
            // Not valid JSON, return as-is
            return content;
        }
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
