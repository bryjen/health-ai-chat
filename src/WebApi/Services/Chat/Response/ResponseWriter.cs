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
    private ContentCategory? _currentCategory;

    // Out-of-band tags (e.g. <SymptomCreated>) are emitted directly to the stream during plugin execution
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
            await WrapContentCategory(content.Category, cancellationToken);
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
            await WrapContentCategory(content.Category, cancellationToken);
            await WriteCoreAsync(content, cancellationToken);
        }
    }

    /// <summary>
    /// Complete the response writing (flush, cleanup, etc.).
    /// </summary>
    internal async Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        await CloseContentCategory(cancellationToken);
        await CompleteCoreAsync(cancellationToken);
    }

    /// <summary>
    /// Reset state between responses.
    /// </summary>
    public virtual Task ResetState()
    {
        _currentCategory = null;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Wrap content with category tags (e.g., &lt;Text&gt;, &lt;/Text&gt;).
    /// </summary>
    private async Task WrapContentCategory(ContentCategory category, CancellationToken cancellationToken)
    {
        if (_currentCategory is null)
        {
            _currentCategory = category;
            await WriteRawAsync($"<{category}>\n", cancellationToken);
            return;
        }

        if (category != _currentCategory)
        {
            await WriteRawAsync($"\n</{_currentCategory}>\n\n", cancellationToken);
            _currentCategory = category;
            await WriteRawAsync($"<{category}>\n", cancellationToken);
        }
    }

    /// <summary>
    /// Close the current content category tag.
    /// </summary>
    private async Task CloseContentCategory(CancellationToken cancellationToken)
    {
        if (_currentCategory is null)
            return;

        await WriteRawAsync($"</{_currentCategory}>", cancellationToken);
    }

    /// <summary>
    /// Write raw text (for tags). Exposed internally for middleware use.
    /// </summary>
    internal Task EmitRawAsync(string text, CancellationToken cancellationToken) => WriteRawAsync(text, cancellationToken);

    /// <summary>
    /// Emits a structured tag to the stream and records it for persistence.
    /// </summary>
    internal async Task EmitRawAsync(string tag, string serialized, CancellationToken cancellationToken)
    {
        var raw = $"<{tag}>{serialized}</{tag}>\n";
        _emittedRaw.Add(raw);
        await WriteRawAsync(raw, cancellationToken);
    }

    /// <summary>
    /// Returns all out-of-band tags emitted this turn and clears the buffer.
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
}
