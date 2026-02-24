using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using WebApi.Services.Chat.Formatters;

namespace WebApi.Services.Chat.Response;

/// <summary>
/// Base class for writing agent responses. Uses MessageFormatter for transformation,
/// delegates presentation to concrete implementations.
/// </summary>
public abstract class ResponseWriter(MessageFormatter formatter)
{
    // Out-of-band events (e.g. SymptomCreated) are emitted directly to the stream during plugin execution
    // and are never part of any ChatMessage. We buffer them here so DbChatHistoryProvider can append them
    // to the assistant message before persisting, making them available on conversation reload.
    private readonly List<string> _emittedRaw = [];

    /// <summary>
    /// Write a streaming update chunk.
    /// </summary>
    internal async Task WriteChunkAsync(AgentResponseUpdate update, CancellationToken cancellationToken = default)
    {
        var formatted = formatter.FormatUpdate(update);
        foreach (var content in formatted)
        {
            if (MessageFormatter.ShouldSkip(content.Category))
                continue;
            await WriteCoreAsync(content, cancellationToken);
        }
    }

    /// <summary>
    /// Write a stored message (for history display).
    /// Uses same formatting pipeline as streaming.
    /// </summary>
    internal async Task WriteMessageAsync(ChatMessage message, CancellationToken cancellationToken = default)
    {
        var formatted = formatter.FormatMessage(message);
        foreach (var content in formatted)
        {
            if (MessageFormatter.ShouldSkip(content.Category))
                continue;
            await WriteCoreAsync(content, cancellationToken);
        }
    }

    /// <summary>
    /// Complete the response writing (flush, cleanup, etc.).
    /// </summary>
    internal async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        await CompleteCoreAsync(cancellationToken);
    }

    /// <summary>
    /// Reset state between responses.
    /// </summary>
    public virtual Task ResetState() => Task.CompletedTask;

    /// <summary>
    /// Emits a structured NDJSON event to the stream and records it for persistence.
    /// </summary>
    internal async Task EmitRawAsync(string tag, string serialized, CancellationToken cancellationToken)
    {
        var type = ToSnakeCase(tag);
        string line;
        if (serialized.TrimStart().StartsWith('{'))
            line = $"{{\"type\":\"{type}\",\"data\":{serialized}}}\n";
        else
            line = $"{{\"type\":\"{type}\",\"name\":{JsonSerializer.Serialize(serialized)}}}\n";

        _emittedRaw.Add(line);
        await WriteRawAsync(line, cancellationToken);
    }

    /// <summary>
    /// Returns all out-of-band events emitted this turn and clears the buffer.
    /// Call this once after the agent run completes, before persisting.
    /// </summary>
    internal IReadOnlyList<string> DrainEmittedRaw()
    {
        var copy = _emittedRaw.ToList();
        _emittedRaw.Clear();
        return copy;
    }

    protected abstract Task WriteRawAsync(string text, CancellationToken cancellationToken);

    /// <summary>
    /// Concrete implementations handle platform-specific rendering.
    /// </summary>
    protected abstract Task WriteCoreAsync(FormattedContent content, CancellationToken cancellationToken);

    /// <summary>
    /// Concrete implementations handle platform-specific completion.
    /// </summary>
    protected abstract Task CompleteCoreAsync(CancellationToken cancellationToken);

    private static string ToSnakeCase(string s) =>
        Regex.Replace(s, "(?<=.)([A-Z])", "_$1").ToLower();
}
