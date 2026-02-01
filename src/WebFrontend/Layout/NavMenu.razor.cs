using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Web.Common.DTOs.Conversations;
using WebApi.ApiWrapper.Services;
using WebFrontend.Components.UI.DropdownMenu;
using WebFrontend.Services;
using WebFrontend.Services.Auth;

namespace WebFrontend.Layout;

[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
public partial class NavMenu : ComponentBase, IDisposable
{
    [Inject] private AuthService AuthService { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private AuthenticationStateProvider AuthStateProvider { get; set; } = null!;
    [Inject] private IConversationsApiClient ConversationsApiClient { get; set; } = null!;
    [Inject] private DropdownService DropdownService { get; set; } = null!;
    [Inject] private ToastService ToastService { get; set; } = null!;
    [Inject] private ChatHubClient ChatHubClient { get; set; } = null!;

    protected bool CollapseNavMenu { get; set; } = true;
    protected List<ConversationSummaryDto> Conversations { get; set; } = new();
    protected string SearchQuery { get; set; } = string.Empty;
    protected bool IsLoadingConversations { get; set; } = false;
    protected Guid? OpenDropdownId { get; set; }
    protected bool IsDeleting { get; set; } = false;

    protected List<ConversationSummaryDto> FilteredConversations
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SearchQuery))
                return Conversations;

            var query = SearchQuery.ToLowerInvariant();
            return Conversations
                .Where(c =>
                    c.Title.ToLowerInvariant().Contains(query) ||
                    (c.LastMessagePreview?.ToLowerInvariant().Contains(query) ?? false))
                .ToList();
        }
    }

    protected override async Task OnInitializedAsync()
    {
        await LoadConversationsAsync();

        // Refresh conversations when navigation occurs
        Navigation.LocationChanged += OnLocationChanged;
    }

    private async void OnLocationChanged(object? sender, Microsoft.AspNetCore.Components.Routing.LocationChangedEventArgs e)
    {
        // Refresh conversations when navigating to chat or when coming back
        if (e.Location.Contains("/chat") || e.Location == Navigation.BaseUri)
        {
            await LoadConversationsAsync();
        }
    }

    protected async Task LoadConversationsAsync()
    {
        var authState = await AuthStateProvider.GetAuthenticationStateAsync();
        if (!authState.User.Identity?.IsAuthenticated ?? true)
        {
            Conversations.Clear();
            return;
        }

        IsLoadingConversations = true;
        try
        {
            Conversations = await ConversationsApiClient.GetAllConversationsAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading conversations: {ex.Message}");
            Conversations = new List<ConversationSummaryDto>();
        }
        finally
        {
            IsLoadingConversations = false;
            StateHasChanged();
        }
    }

    protected void ToggleNavMenu()
    {
        CollapseNavMenu = !CollapseNavMenu;
    }

    protected async Task HandleLogout()
    {
        // Disconnect SignalR connection
        await ChatHubClient.DisconnectAsync();
        
        // Clear all authentication data
        await AuthService.LogoutAsync();
        
        // Notify authentication state change
        if (AuthStateProvider is AuthStateProvider provider)
        {
            provider.NotifyUserChanged();
        }
        
        // Clear conversations
        Conversations.Clear();
        
        // Navigate to login page with force reload
        Navigation.NavigateTo("/login", forceLoad: true);
    }

    protected void StartNewChat()
    {
        Navigation.NavigateTo("/chat");
    }

    protected string GetUsername(AuthenticationState authState)
    {
        // Try to get from AuthService first (more reliable - has email)
        var user = AuthService.CurrentUser;
        if (user != null && !string.IsNullOrEmpty(user.Email))
        {
            return user.Email;
        }

        // Fallback to claims (email claim)
        var emailClaim = authState.User?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
        if (!string.IsNullOrEmpty(emailClaim))
        {
            return emailClaim;
        }

        // Last fallback to name claim
        return authState.User?.Identity?.Name ?? "User";
    }

    protected async Task ToggleDropdownAsync(Guid conversationId)
    {
        if (OpenDropdownId == conversationId)
        {
            await CloseDropdownAsync();
        }
        else
        {
            await OpenDropdownAsync(conversationId);
        }
    }

    protected async Task OpenDropdownAsync(Guid conversationId)
    {
        OpenDropdownId = conversationId;
        var dropdownId = conversationId.ToString();

        // Find the conversation to get its title for the dropdown content
        var conversation = Conversations.FirstOrDefault(c => c.Id == conversationId);
        if (conversation == null) return;

        await DropdownService.OpenDropdownAsync(dropdownId, async () =>
        {
            OpenDropdownId = null;
            await InvokeAsync(StateHasChanged);
        }, RenderConversationDropdown(conversationId));

        StateHasChanged();
    }

    protected async Task CloseDropdownAsync()
    {
        if (OpenDropdownId.HasValue)
        {
            var dropdownId = OpenDropdownId.Value.ToString();
            await DropdownService.CloseDropdownAsync(dropdownId);
            OpenDropdownId = null;
            StateHasChanged();
        }
    }

    protected async Task HandleDeleteConversation(Guid conversationId)
    {
        if (IsDeleting)
            return;

        IsDeleting = true;
        await CloseDropdownAsync();

        try
        {
            await ConversationsApiClient.DeleteConversationAsync(conversationId);
            await LoadConversationsAsync();

            ToastService.ShowSuccess("Conversation deleted", "The conversation has been permanently removed.");

            // If we're currently viewing this conversation, navigate away
            var currentUri = Navigation.Uri;
            if (currentUri.Contains($"conversation={conversationId}"))
            {
                Navigation.NavigateTo("/chat");
            }
        }
        catch (Exception ex)
        {
            ToastService.ShowError("Failed to delete conversation", ex.Message);
        }
        finally
        {
            IsDeleting = false;
        }
    }

    public void Dispose()
    {
        Navigation.LocationChanged -= OnLocationChanged;
    }
}
