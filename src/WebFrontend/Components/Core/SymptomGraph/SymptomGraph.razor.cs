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
    private string ElementId { get; set; } = Guid.NewGuid().ToString();
    private bool IsLoading { get; set; } = true;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            try
            {
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
            // Update graph when data changes
            await UpdateGraphAsync();
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

            await _jsModule.InvokeVoidAsync("initializeGraph", ElementId, jsonData);
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

    public async ValueTask DisposeAsync()
    {
        if (_jsModule != null)
        {
            try
            {
                await _jsModule.InvokeVoidAsync("disposeGraph", ElementId);
                await _jsModule.DisposeAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error disposing graph: {ex.Message}");
            }
        }
    }
}
