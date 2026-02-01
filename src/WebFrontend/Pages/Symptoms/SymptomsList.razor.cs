using Microsoft.AspNetCore.Components;
using WebApi.ApiWrapper.Services;

namespace WebFrontend.Pages.Symptoms;

public partial class SymptomsList : ComponentBase
{
    [Inject] private ISymptomsApiClient SymptomsApiClient { get; set; } = default!;

    private List<Web.Common.DTOs.Health.SymptomDto>? Symptoms { get; set; }
    private bool IsLoading { get; set; } = true;
    private string? ErrorMessage { get; set; }

    protected override async Task OnInitializedAsync()
    {
        try
        {
            Symptoms = await SymptomsApiClient.GetSymptomsAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load symptoms: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
