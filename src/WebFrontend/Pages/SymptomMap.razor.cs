using Microsoft.AspNetCore.Components;
using Web.Common.DTOs.Health;
using WebApi.ApiWrapper.Services;

namespace WebFrontend.Pages;

public partial class SymptomMap : ComponentBase
{
    [Inject] private IEpisodesApiClient EpisodesApiClient { get; set; } = null!;
    [Inject] private IAssessmentsApiClient AssessmentsApiClient { get; set; } = null!;

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

    // Convert episodes to display format
    private List<SymptomDisplay> DetectedSymptoms
    {
        get
        {
            return ActiveEpisodes
                .GroupBy(e => e.SymptomName)
                .Select(g => new SymptomDisplay
                {
                    Name = g.Key,
                    Icon = GetSymptomIcon(g.Key),
                    IconColor = GetSeverityColor(g.Max(e => e.Severity ?? 0)),
                    Details = FormatEpisodeDetails(g.OrderByDescending(e => e.StartedAt).First())
                })
                .ToList();
        }
    }

    // Convert episodes to visual nodes
    private List<SymptomNode> SymptomNodes
    {
        get
        {
            var nodes = new List<SymptomNode>();
            var positions = GeneratePositions(ActiveEpisodes.Count);
            var index = 0;

            foreach (var episode in ActiveEpisodes.OrderByDescending(e => e.Severity ?? 0))
            {
                if (index >= positions.Count) break;

                var pos = positions[index];
                var severity = episode.Severity ?? 0;
                var size = GetNodeSize(severity);
                var color = GetSeverityColor(severity);

                nodes.Add(new SymptomNode
                {
                    Left = pos.X,
                    Top = pos.Y,
                    SizeClass = size,
                    Style = severity >= 7 
                        ? $"background-color: {color};" 
                        : $"border: 1px solid {color}; background-color: #0b0b0b;",
                    GlowClass = severity >= 7 ? "node-glow" : "",
                    Icon = GetSymptomIcon(episode.SymptomName),
                    IconSizeClass = GetIconSize(size),
                    IconColor = $"color: {(severity >= 7 ? "white" : color)};",
                    Label = severity >= 7 ? $"{episode.SymptomName.ToUpper()} ({severity})" : "",
                    Tooltip = FormatEpisodeTooltip(episode),
                    HoverClass = severity >= 7 ? "" : "node-hover-neon"
                });

                index++;
            }

            return nodes;
        }
    }

    // Convert assessments to diagnostic inferences
    private List<DiagnosticInference> DiagnosticInferences
    {
        get
        {
            return RecentAssessments
                .Select(a => new DiagnosticInference
                {
                    Name = a.Hypothesis,
                    Percentage = (int)(a.Confidence * 100),
                    PercentageColor = GetActionColor(a.RecommendedAction),
                    BarColor = GetActionColor(a.RecommendedAction)
                })
                .ToList();
        }
    }

    private string GetSymptomIcon(string symptomName)
    {
        return symptomName.ToLower() switch
        {
            var s when s.Contains("fever") => "thermometer",
            var s when s.Contains("cough") => "airwave",
            var s when s.Contains("headache") => "psychology",
            var s when s.Contains("nausea") => "sentiment_dissatisfied",
            var s when s.Contains("pain") => "healing",
            _ => "medical_services"
        };
    }

    private string GetSeverityColor(int severity)
    {
        return severity switch
        {
            >= 8 => "#5288ed", // High severity - blue
            >= 5 => "#8e8e8e", // Medium severity - gray
            _ => "#8e8e8e"     // Low severity - gray
        };
    }

    private string GetActionColor(string action)
    {
        return action switch
        {
            "emergency" => "#ef4444",
            "urgent-care" => "#f59e0b",
            "see-gp" => "#5288ed",
            _ => "#8e8e8e"
        };
    }

    private string FormatEpisodeDetails(EpisodeDto episode)
    {
        var parts = new List<string>();
        
        if (episode.Severity.HasValue)
        {
            parts.Add($"Severity: {episode.Severity}/10");
        }
        
        if (!string.IsNullOrEmpty(episode.Frequency))
        {
            parts.Add(episode.Frequency);
        }
        
        if (!string.IsNullOrEmpty(episode.Location))
        {
            parts.Add(episode.Location);
        }

        return parts.Any() ? string.Join(" • ", parts) : episode.Stage;
    }

    private string FormatEpisodeTooltip(EpisodeDto episode)
    {
        var details = new List<string> { $"Stage: {episode.Stage}" };
        
        if (episode.Severity.HasValue)
        {
            details.Add($"Severity: {episode.Severity}/10");
        }
        
        if (!string.IsNullOrEmpty(episode.Frequency))
        {
            details.Add($"Frequency: {episode.Frequency}");
        }
        
        if (!string.IsNullOrEmpty(episode.Location))
        {
            details.Add($"Location: {episode.Location}");
        }
        
        if (episode.Triggers?.Any() == true)
        {
            details.Add($"Triggers: {string.Join(", ", episode.Triggers)}");
        }

        return string.Join(". ", details);
    }

    private string GetNodeSize(int severity)
    {
        return severity switch
        {
            >= 8 => "size-14",
            >= 5 => "size-10",
            _ => "size-8"
        };
    }

    private string GetIconSize(string sizeClass)
    {
        return sizeClass switch
        {
            "size-14" => "text-2xl",
            "size-10" => "text-lg",
            _ => "text-sm"
        };
    }

    private List<(double X, double Y)> GeneratePositions(int count)
    {
        // Generate positions in a circular/spiral pattern
        var positions = new List<(double X, double Y)>();
        var centerX = 50.0;
        var centerY = 50.0;
        var radius = 15.0;

        for (int i = 0; i < count; i++)
        {
            var angle = (2 * Math.PI * i) / Math.Max(count, 1);
            var x = centerX + radius * Math.Cos(angle) + (i % 3 - 1) * 5;
            var y = centerY + radius * Math.Sin(angle) + (i % 2) * 3;
            positions.Add((x, y));
        }

        return positions;
    }

    // Hardcoded graph data matching the reference implementation
    private GraphDataDto GraphData => GetHardcodedGraphData();

    private GraphDataDto GetHardcodedGraphData()
    {
        return new GraphDataDto
        {
            Nodes = new List<GraphNodeDto>
            {
                new GraphNodeDto { Id = "root", Label = "Fever 102.4", Type = GraphNodeType.Root, Value = 40, Group = 1 },
                new GraphNodeDto { Id = "inf", Label = "Influenza Cluster", Type = GraphNodeType.Diagnosis, Value = 30, Group = 2 },
                new GraphNodeDto { Id = "cough", Label = "Cough", Type = GraphNodeType.Symptom, Value = 20, Group = 3 },
                new GraphNodeDto { Id = "headache", Label = "Headache", Type = GraphNodeType.Symptom, Value = 20, Group = 3 },
                new GraphNodeDto { Id = "chills", Label = "Chills", Type = GraphNodeType.Symptom, Value = 15, Group = 3 },
                new GraphNodeDto { Id = "sweats", Label = "Night Sweats", Type = GraphNodeType.Symptom, Value = 15, Group = 3 },
                new GraphNodeDto { Id = "fatigue", Label = "Fatigue", Type = GraphNodeType.Symptom, Value = 15, Group = 3 }
            },
            Links = new List<GraphLinkDto>
            {
                new GraphLinkDto { Source = "root", Target = "inf", Value = 5 },
                new GraphLinkDto { Source = "inf", Target = "cough", Value = 3 },
                new GraphLinkDto { Source = "inf", Target = "headache", Value = 3 },
                new GraphLinkDto { Source = "root", Target = "chills", Value = 4 },
                new GraphLinkDto { Source = "root", Target = "sweats", Value = 4 },
                new GraphLinkDto { Source = "inf", Target = "fatigue", Value = 2 },
                new GraphLinkDto { Source = "headache", Target = "root", Value = 2 }
            }
        };
    }

    private class SymptomDisplay
    {
        public string Name { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string IconColor { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
    }

    private class SymptomNode
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public string SizeClass { get; set; } = string.Empty;
        public string Style { get; set; } = string.Empty;
        public string GlowClass { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string IconSizeClass { get; set; } = string.Empty;
        public string IconColor { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public string Tooltip { get; set; } = string.Empty;
        public string HoverClass { get; set; } = string.Empty;
    }

    private class DiagnosticInference
    {
        public string Name { get; set; } = string.Empty;
        public int Percentage { get; set; }
        public string PercentageColor { get; set; } = string.Empty;
        public string BarColor { get; set; } = string.Empty;
    }
}
