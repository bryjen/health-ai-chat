using System.Text;
using System.Text.Json;
using Web.Common.DTOs.Health;
using WebFrontend.Components.Chat.Models;

namespace WebFrontend.Components.Chat.Services.StreamResponse;

public class StreamResponseParser(IList<MessageComponent> components, JsonSerializerOptions jsonOptions) : IStreamResponseParser
{
    private readonly StringBuilder _buffer = new();
    private MessageComponent? _activeComponent;
    private string? _activeType;

    public void AppendChunk(string chunk)
    {
        if (string.IsNullOrEmpty(chunk))
            return;

        _buffer.Append(chunk);
        var text = _buffer.ToString();
        var lines = text.Split('\n');

        // All complete lines (all but the last incomplete fragment)
        for (var i = 0; i < lines.Length - 1; i++)
            ProcessLine(lines[i].Trim());

        // Keep the incomplete last fragment in buffer
        _buffer.Clear();
        _buffer.Append(lines[^1]);
    }

    private void ProcessLine(string line)
    {
        if (string.IsNullOrEmpty(line)) return;

        try
        {
            var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeEl)) return;
            var type = typeEl.GetString();

            switch (type)
            {
                case "text":
                    AppendTextContent(type, root, () => new TextMessageComponent { Content = string.Empty },
                        c => ((TextMessageComponent)c).Content,
                        (c, v) => ((TextMessageComponent)c).Content = v);
                    break;

                case "reasoning":
                    AppendTextContent(type, root, () => new ThinkingMessageComponent { Content = string.Empty },
                        c => ((ThinkingMessageComponent)c).Content,
                        (c, v) => ((ThinkingMessageComponent)c).Content = v);
                    break;

                case "search":
                    AppendTextContent(type, root, () => new WebSearchMessageComponent { Content = string.Empty },
                        c => ((WebSearchMessageComponent)c).Content,
                        (c, v) => ((WebSearchMessageComponent)c).Content = v);
                    break;

                case "tool_call":
                    var name = root.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? string.Empty : string.Empty;
                    FinalizeActiveComponent();
                    var tc = new ToolCallMessageComponent { FunctionName = name };
                    components.Add(tc);
                    _activeType = null;
                    _activeComponent = null;
                    break;

                case "symptom_created":
                    FinalizeActiveComponent();
                    var sc = new SymptomCreatedMessageComponent { RawJson = string.Empty };
                    if (root.TryGetProperty("data", out var scData))
                    {
                        sc.RawJson = scData.GetRawText();
                        ParseSymptomCreatedJson(sc);
                    }
                    components.Add(sc);
                    _activeType = null;
                    _activeComponent = null;
                    break;

                case "assessment_created":
                    FinalizeActiveComponent();
                    var ac = new AssessmentCreatedMessageComponent { RawJson = string.Empty };
                    if (root.TryGetProperty("data", out var acData))
                    {
                        ac.RawJson = acData.GetRawText();
                        ParseAssessmentCreatedJson(ac);
                    }
                    components.Add(ac);
                    _activeType = null;
                    _activeComponent = null;
                    break;
            }
        }
        catch { /* ignore malformed lines */ }
    }

    private void AppendTextContent(
        string type,
        JsonElement root,
        Func<MessageComponent> create,
        Func<MessageComponent, string> getContent,
        Action<MessageComponent, string> setContent)
    {
        var content = root.TryGetProperty("content", out var el) ? el.GetString() ?? string.Empty : string.Empty;

        if (_activeType == type && _activeComponent != null)
        {
            setContent(_activeComponent, getContent(_activeComponent) + content);
        }
        else
        {
            FinalizeActiveComponent();
            var component = create();
            setContent(component, content);
            _activeType = type;
            _activeComponent = component;
            components.Add(component);
        }
    }

    private void FinalizeActiveComponent()
    {
        if (_activeComponent is ThinkingMessageComponent thinking && thinking.ThinkingTime == 0)
        {
            var rand = new Random();
            const int min = 5, max = 10;
            thinking.ThinkingTime = rand.Next(min, max + 1);
        }
    }

    private void ParseAssessmentCreatedJson(AssessmentCreatedMessageComponent component)
    {
        try { component.Status = JsonSerializer.Deserialize<AssessmentCreatedStatus>(component.RawJson, jsonOptions); } catch { }
    }

    private void ParseSymptomCreatedJson(SymptomCreatedMessageComponent component)
    {
        try { component.Status = JsonSerializer.Deserialize<SymptomCreatedStatus>(component.RawJson, jsonOptions); } catch { }
    }
}
