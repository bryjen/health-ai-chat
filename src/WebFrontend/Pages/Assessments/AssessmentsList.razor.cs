using Microsoft.AspNetCore.Components;
using WebApi.ApiWrapper.Services;

namespace WebFrontend.Pages.Assessments;

public partial class AssessmentsList : ComponentBase
{
    [Inject] private IAssessmentsApiClient AssessmentsApiClient { get; set; } = default!;

    private List<Web.Common.DTOs.Health.AssessmentDto>? Assessments { get; set; }
    private bool IsLoading { get; set; } = true;
    private string? ErrorMessage { get; set; }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            Assessments = await AssessmentsApiClient.GetRecentAssessmentsAsync(limit: 50);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load assessments: {ex.Message}";
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
}
