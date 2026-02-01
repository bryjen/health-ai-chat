using Microsoft.AspNetCore.Components;
using WebApi.ApiWrapper.Services;

namespace WebFrontend.Pages.Assessments;

public partial class AssessmentDetail : ComponentBase
{
    [Inject] private IAssessmentsApiClient AssessmentsApiClient { get; set; } = default!;
    [Inject] private NavigationManager Navigation { get; set; } = default!;

    [Parameter]
    public int Id { get; set; }

    private Web.Common.DTOs.Health.AssessmentDto? Assessment { get; set; }
    private bool IsLoading { get; set; } = true;
    private string? ErrorMessage { get; set; }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            Assessment = await AssessmentsApiClient.GetAssessmentByIdAsync(Id);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load assessment: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private string GetRecommendedActionDisplay(string action)
    {
        return action switch
        {
            "self-care" => "Self Care",
            "see-gp" => "See GP",
            "urgent-care" => "Urgent Care",
            "emergency" => "Emergency",
            _ => action
        };
    }

    private void NavigateBack()
    {
        Navigation.NavigateTo("/chat");
    }
}
