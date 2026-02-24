using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace WebApi.Services.Chat.Formatters;

/// <summary>
/// Platform-agnostic message formatter that transforms content into structured format objects.
/// Works for both streaming (AgentResponseUpdate) and stored messages (ChatMessage).
/// </summary>
public class MessageFormatter
{
    /// <summary>
    /// Format a streaming update into structured content for display.
    /// </summary>
    public IEnumerable<FormattedContent> FormatUpdate(AgentResponseUpdate update)
    {
        foreach (var content in update.Contents)
        {
            var text = ExtractText(content);
            if (string.IsNullOrEmpty(text))
                continue;

            var category = CategorizeContent(content);
            yield return new FormattedContent(category, text, update.Role);
        }
    }

    /// <summary>
    /// Format a stored message into structured content for display.
    /// </summary>
    public IEnumerable<FormattedContent> FormatMessage(ChatMessage message)
    {
        foreach (var content in message.Contents)
        {
            var text = ExtractText(content);
            if (string.IsNullOrEmpty(text))
                continue;

            var category = CategorizeContent(content);
            yield return new FormattedContent(category, text, message.Role, ExtractMetadata(content));
        }
    }

    /// <summary>
    /// Extract displayable text from content.
    /// </summary>
    private string ExtractText(AIContent content)
    {
        return content switch
        {
            TextContent textContent => textContent.Text,
            TextReasoningContent reasoningContent => reasoningContent.Text,
            FunctionCallContent functionCall => functionCall.Name,
            FunctionResultContent functionResult => functionResult.Result?.ToString() ?? string.Empty,
            UsageContent usage => FormatUsage(usage),
            _ => string.Empty
        };
    }

    /// <summary>
    /// Categorize content for rendering purposes.
    /// </summary>
    private ContentCategory CategorizeContent(AIContent content)
    {
        return content switch
        {
            TextContent => ContentCategory.Text,
            TextReasoningContent => ContentCategory.Reasoning,
            FunctionCallContent => ContentCategory.ToolCall,
            FunctionResultContent => ContentCategory.ToolResult,
            UsageContent => ContentCategory.Usage,
            _ => ContentCategory.Unknown
        };
    }

    /// <summary>
    /// Extract metadata from content (e.g., function call ID).
    /// </summary>
    private string? ExtractMetadata(AIContent content)
    {
        return content switch
        {
            FunctionCallContent functionCall => functionCall.CallId,
            FunctionResultContent functionResult => functionResult.CallId,
            _ => null
        };
    }

    /// <summary>
    /// Format usage information.
    /// </summary>
    private string FormatUsage(UsageContent usage)
    {
        var details = usage.Details;
        return $"Input: {details.InputTokenCount}, Output: {details.OutputTokenCount}, Total: {details.TotalTokenCount}";
    }
}

/// <summary>
/// Structured representation of formatted content.
/// </summary>
public record FormattedContent(
    ContentCategory Category,
    string Text,
    ChatRole? Role = null,
    string? Metadata = null
);

/// <summary>
/// Categories for different types of content.
/// </summary>
public enum ContentCategory
{
    Text,
    Reasoning,
    ToolCall,
    ToolResult,
    Usage,
    Unknown
}
