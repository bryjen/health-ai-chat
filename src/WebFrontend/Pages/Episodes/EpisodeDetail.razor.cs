using Microsoft.AspNetCore.Components;
using WebApi.ApiWrapper.Services;

namespace WebFrontend.Pages.Episodes;

public partial class EpisodeDetail : ComponentBase
{
    [Inject] private IEpisodesApiClient EpisodesApiClient { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [Parameter]
    public int Id { get; set; }

    private Web.Common.DTOs.Health.EpisodeDto? Episode { get; set; }
    private bool IsLoading { get; set; } = true;
    private string? ErrorMessage { get; set; }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            Episode = await EpisodesApiClient.GetEpisodeAsync(Id);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load episode: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private string GetStatusDisplay(string status)
    {
        return status switch
        {
            "active" => "Active",
            "resolved" => "Resolved",
            "chronic" => "Chronic",
            _ => status
        };
    }

    private string GetStatusClass(string status)
    {
        return status switch
        {
            "active" => "bg-yellow-500/10 text-yellow-600 dark:text-yellow-400",
            "resolved" => "bg-green-500/10 text-green-600 dark:text-green-400",
            "chronic" => "bg-orange-500/10 text-orange-600 dark:text-orange-400",
            _ => "bg-muted text-foreground"
        };
    }

    private string GetStageDisplay(string stage)
    {
        return stage switch
        {
            "mentioned" => "Mentioned",
            "explored" => "Explored",
            "characterized" => "Characterized",
            "linked" => "Linked",
            _ => stage
        };
    }

    private string GetFrequencyDisplay(string frequency)
    {
        return frequency switch
        {
            "constant" => "Constant",
            "intermittent" => "Intermittent",
            "occasional" => "Occasional",
            _ => frequency
        };
    }

    private void NavigateBack()
    {
        Navigation.NavigateTo("/chat");
    }
}
