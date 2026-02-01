using Microsoft.AspNetCore.Components;
using Web.Common.DTOs.Health;
using WebApi.ApiWrapper.Services;

namespace WebFrontend.Pages;

public partial class Dashboard : ComponentBase
{
    [Inject] private IEpisodesApiClient EpisodesApiClient { get; set; } = null!;
    [Inject] private IAssessmentsApiClient AssessmentsApiClient { get; set; } = null!;

    private string SearchQuery { get; set; } = string.Empty;
    private List<EpisodeDto> ActiveEpisodes { get; set; } = new();
    private List<AssessmentDto> RecentAssessments { get; set; } = new();
    private bool IsLoading { get; set; } = true;
    private string? ErrorMessage { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;

            // Load active episodes
            ActiveEpisodes = await EpisodesApiClient.GetActiveEpisodesAsync(days: 14);

            // Load recent assessments
            RecentAssessments = await AssessmentsApiClient.GetRecentAssessmentsAsync(limit: 5);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to load data: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    // Convert assessments to conditions display
    private List<Condition> Conditions
    {
        get
        {
            return RecentAssessments
                .Select(a => new Condition
                {
                    Name = a.Hypothesis,
                    Icon = GetConditionIcon(a.Hypothesis),
                    Confidence = (int)(a.Confidence * 100),
                    ConfidenceColor = GetConfidenceColor(a.Confidence),
                    ProgressColor = GetProgressColor(a.Confidence),
                    ShadowClass = a.Confidence >= 0.7m ? "shadow-[0_0_10px_rgba(16,185,129,0.3)]" : "",
                    Description = a.Reasoning.Length > 100 ? a.Reasoning.Substring(0, 100) + "..." : a.Reasoning
                })
                .OrderByDescending(c => c.Confidence)
                .ToList();
        }
    }

    // Convert episodes to symptom severity chart data
    private List<SymptomDay> SymptomDays
    {
        get
        {
            var days = new List<string> { "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN" };
            var now = DateTime.UtcNow;
            var startOfWeek = now.AddDays(-(int)now.DayOfWeek + 1); // Monday

            return days.Select((day, index) =>
            {
                var date = startOfWeek.AddDays(index);
                var dayEpisodes = ActiveEpisodes
                    .Where(e => e.StartedAt.Date <= date.Date && (e.ResolvedAt == null || e.ResolvedAt.Value.Date >= date.Date))
                    .ToList();

                var maxSeverity = dayEpisodes.Any() ? dayEpisodes.Max(e => e.Severity ?? 0) : 0;
                var height = maxSeverity * 10; // Scale to percentage
                var isHighSeverity = maxSeverity >= 7;

                return new SymptomDay
                {
                    Label = day,
                    Height = Math.Max(height, 10), // Minimum height for visibility
                    BarClass = isHighSeverity ? "bg-blue-500" : maxSeverity >= 4 ? "bg-blue-500/40" : "bg-zinc-800",
                    TextClass = isHighSeverity ? "text-zinc-200" : "text-zinc-600",
                    ShadowClass = isHighSeverity ? "shadow-[0_0_20px_rgba(59,130,246,0.2)]" : ""
                };
            }).ToList();
        }
    }

    private string GetConditionIcon(string hypothesis)
    {
        var lower = hypothesis.ToLower();
        return lower switch
        {
            var h when h.Contains("allerg") => "thermometer",
            var h when h.Contains("migraine") || h.Contains("headache") => "psychology",
            var h when h.Contains("cold") || h.Contains("flu") => "air",
            var h when h.Contains("fever") => "thermometer",
            var h when h.Contains("cough") => "airwave",
            _ => "medical_services"
        };
    }

    private string GetConfidenceColor(decimal confidence)
    {
        return confidence switch
        {
            >= 0.7m => "text-emerald-400",
            >= 0.5m => "text-amber-400",
            _ => "text-zinc-400"
        };
    }

    private string GetProgressColor(decimal confidence)
    {
        return confidence switch
        {
            >= 0.7m => "bg-emerald-500",
            >= 0.5m => "bg-amber-500",
            _ => "bg-zinc-500"
        };
    }

    private class Condition
    {
        public string Name { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public int Confidence { get; set; }
        public string ConfidenceColor { get; set; } = string.Empty;
        public string ProgressColor { get; set; } = string.Empty;
        public string ShadowClass { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    private class SymptomDay
    {
        public string Label { get; set; } = string.Empty;
        public int Height { get; set; }
        public string BarClass { get; set; } = string.Empty;
        public string TextClass { get; set; } = string.Empty;
        public string ShadowClass { get; set; } = string.Empty;
    }

    // Keep specialists as mock data for now (could be replaced with real API later)
    private List<Specialist> Specialists { get; set; } = new()
    {
        new Specialist
        {
            Name = "Dr. Sarah Chen",
            Specialty = "Immunologist",
            SpecialtyColor = "text-blue-400",
            Availability = "2.4 miles • Today 2:00 PM",
            LocationIcon = "location_on",
            ButtonText = "Book Appointment",
            ButtonClass = "bg-white text-black",
            ImageUrl = "https://lh3.googleusercontent.com/aida-public/AB6AXuCG0QPwDiGHIyKH3Mz3I61wwV0P4O6sIH-u0eXZIZOdSMqpsLjb5OZsPybOosAVhb6ApIbt1TJC4zo7IvadzxZEEv2OxNcZs_D-59MJTRJu1Ndr9pEIndeFulT4YxfQXVK1MEYYxXQKHA2v5_uFTuhxjG7IPUbKa6e7YDikBSQx5SegWHYiBAvts3jgeSVAqb2GH3odVHaRzFs96XbJuafx_uJqmsFOCJH3otE-rDoEmRnxit0QRI35B90pg-SjAlTz24ks2s6Kl5U"
        },
        new Specialist
        {
            Name = "Dr. Marcus Thorne",
            Specialty = "Neurologist",
            SpecialtyColor = "text-amber-500",
            Availability = "Virtual Available • Tomorrow",
            LocationIcon = "videocam",
            ButtonText = "Request Virtual Visit",
            ButtonClass = "border border-zinc-700 text-zinc-300 hover:text-white hover:border-zinc-500",
            ImageUrl = "https://lh3.googleusercontent.com/aida-public/AB6AXuCKHaXHJXPitFa8fiyEGDqfQ18NUnYBUUa99C3nkdjYG-8sQKXutczeUlQfOUimOTq7zDsY8lPiaiIDaVPIx-vvBTqANgaUSQFkA6sDIkROPGpGD1JHFaQXfvZDKnKxksAeC2TW8DNux8NsoFik3xiqOKt_Kv_GP93Q-8aBO3_SlxahBSELz6iAhBs9buTJKZTEXJgxPa-c3wtByjS06v-rV2_r4LmdpZjuNXR1gra5cKVGismE7mIZ4p2M5NsSel2WtLPBSFaClVs"
        }
    };

    private class Specialist
    {
        public string Name { get; set; } = string.Empty;
        public string Specialty { get; set; } = string.Empty;
        public string SpecialtyColor { get; set; } = string.Empty;
        public string Availability { get; set; } = string.Empty;
        public string LocationIcon { get; set; } = string.Empty;
        public string ButtonText { get; set; } = string.Empty;
        public string ButtonClass { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
    }
}
