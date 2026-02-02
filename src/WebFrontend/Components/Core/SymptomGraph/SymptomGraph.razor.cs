using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using System.Text.Json;
using System.Text.Json.Serialization;
using Web.Common.DTOs.Health;

namespace WebFrontend.Components.Core.SymptomGraph;

public class LowercaseJsonStringEnumConverter : JsonConverter<GraphNodeType>
{
    public override GraphNodeType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (string.IsNullOrEmpty(value)) return GraphNodeType.Symptom;
        
        return value.ToLower() switch
        {
            "root" => GraphNodeType.Root,
            "diagnosis" => GraphNodeType.Diagnosis,
            "symptom" => GraphNodeType.Symptom,
            _ => GraphNodeType.Symptom
        };
    }

    public override void Write(Utf8JsonWriter writer, GraphNodeType value, JsonSerializerOptions options)
    {
        var stringValue = value switch
        {
            GraphNodeType.Root => "root",
            GraphNodeType.Diagnosis => "diagnosis",
            GraphNodeType.Symptom => "symptom",
            _ => "symptom"
        };
        writer.WriteStringValue(stringValue);
    }
}

public partial class SymptomGraph : ComponentBase, IAsyncDisposable
{
    [Parameter] public GraphDataDto? Data { get; set; }

    [Inject] private IJSRuntime JSRuntime { get; set; } = null!;

    private IJSObjectReference? _jsModule;
    private DotNetObjectReference<SymptomGraph>? _dotNetRef;
    private string ElementId { get; set; } = Guid.NewGuid().ToString();
    private bool IsLoading { get; set; } = true;
    private string? SelectedNodeId { get; set; }
    private string? _lastSelectedNodeId;
    private string? _lastDataHash; // Track data changes to avoid unnecessary updates
    
    private NodeDetails? SelectedNodeDetails => SelectedNodeId != null ? GetNodeDetails(SelectedNodeId) : null;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
                _dotNetRef = DotNetObjectReference.Create(this);
                _jsModule = await JSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/symptom-graph.js");
                await Task.Delay(50); // Wait for DOM to be ready
                await InitializeGraphAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to initialize graph: {ex.Message}");
            }
        }
        else if (Data != null)
        {
            // Only update graph when data actually changes, not on every render
            var currentDataHash = GetDataHash(Data);
            if (currentDataHash != _lastDataHash)
            {
                _lastDataHash = currentDataHash;
                await UpdateGraphAsync();
            }
        }
        
        // Update selected node visual state only when it changes
        if (_jsModule != null && SelectedNodeId != _lastSelectedNodeId)
        {
            await _jsModule.InvokeVoidAsync("setSelectedNode", ElementId, SelectedNodeId);
            _lastSelectedNodeId = SelectedNodeId;
        }
    }

    private async Task InitializeGraphAsync()
    {
        if (_jsModule == null || Data == null) return;

        try
        {
            // Serialize data to JSON for JavaScript
            // Convert enum to lowercase string to match JS expectations
            var jsonData = JsonSerializer.Serialize(Data, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new LowercaseJsonStringEnumConverter() }
            });

            await _jsModule.InvokeVoidAsync("initializeGraph", ElementId, jsonData, _dotNetRef);
            IsLoading = false;
            StateHasChanged();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to initialize graph: {ex.Message}");
            IsLoading = false;
            StateHasChanged();
        }
    }

    private async Task UpdateGraphAsync()
    {
        if (_jsModule == null || Data == null) return;

        try
        {
            var jsonData = JsonSerializer.Serialize(Data, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Converters = { new LowercaseJsonStringEnumConverter() }
            });

            await _jsModule.InvokeVoidAsync("updateGraph", ElementId, jsonData);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to update graph: {ex.Message}");
        }
    }

    [JSInvokable]
    public async Task OnNodeClick(string? nodeId)
    {
        // Only update if selection actually changed
        var newSelection = SelectedNodeId == nodeId ? null : nodeId;
        if (newSelection != SelectedNodeId)
        {
            SelectedNodeId = newSelection;
            await InvokeAsync(StateHasChanged);
        }
    }
    
    private string GetDataHash(GraphDataDto data)
    {
        // Create a simple hash of the data to detect changes
        // This prevents unnecessary graph updates when only selection changes
        if (data == null) return string.Empty;
        
        var nodesHash = data.Nodes?.Count ?? 0;
        var linksHash = data.Links?.Count ?? 0;
        return $"{nodesHash}_{linksHash}";
    }

    private NodeDetails? GetNodeDetails(string nodeId)
    {
        return nodeId switch
        {
            "root" => new NodeDetails
            {
                Description = "Core symptom detected. High-grade pyrexia indicating systemic inflammatory response.",
                Severity = "High",
                Correlation = "98%",
                Onset = "18h ago"
            },
            "inf" => new NodeDetails
            {
                Description = "Probable viral cluster identification. Pattern matches Influenza A strain.",
                Severity = "Critical",
                Correlation = "84%",
                Onset = "Inferred"
            },
            "cough" => new NodeDetails
            {
                Description = "Non-productive cough. Frequent paroxysms reported with chest discomfort.",
                Severity = "Moderate",
                Correlation = "76%",
                Onset = "12h ago"
            },
            "headache" => new NodeDetails
            {
                Description = "Frontal cephalalgia. Intermittent throbbing pain aggravated by light.",
                Severity = "Moderate",
                Correlation = "82%",
                Onset = "16h ago"
            },
            "chills" => new NodeDetails
            {
                Description = "Rigors with visible trembling. Associated with fever spikes.",
                Severity = "Moderate",
                Correlation = "71%",
                Onset = "14h ago"
            },
            "sweats" => new NodeDetails
            {
                Description = "Profuse diaphoresis during sleep. Bedding saturation reported.",
                Severity = "Low",
                Correlation = "65%",
                Onset = "8h ago"
            },
            "fatigue" => new NodeDetails
            {
                Description = "Marked asthenia limiting daily activities. Rest does not alleviate.",
                Severity = "Moderate",
                Correlation = "68%",
                Onset = "24h ago"
            },
            _ => null
        };
    }

    public async ValueTask DisposeAsync()
    {
        if (_jsModule != null)
        {
            try
            {
                // Pass true to cleanup DotNet reference on final disposal
                await _jsModule.InvokeVoidAsync("disposeGraph", ElementId, true);
                await _jsModule.DisposeAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error disposing graph: {ex.Message}");
            }
        }
        _dotNetRef?.Dispose();
    }
    
    private string GetSeverityColorClass(string severity)
    {
        return severity switch
        {
            "Critical" => "text-red-400",
            "High" => "text-orange-400",
            "Moderate" => "text-yellow-400",
            _ => "text-green-400"
        };
    }

    private class NodeDetails
    {
        public required string Description { get; set; }
        public required string Severity { get; set; }
        public required string Correlation { get; set; }
        public required string Onset { get; set; }
    }
}
