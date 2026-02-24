namespace WebFrontend.Components.Chat.Models;

public abstract class MessageComponent;

public class ThinkingMessageComponent : MessageComponent
{
    public required string Content { get; set; }
    public int? ThinkingTime { get; set; }
}

public class TextMessageComponent : MessageComponent
{
    public required string Content { get; set; }
}

public class WebSearchMessageComponent : MessageComponent
{
    public required string Content { get; set; }
}

public class ToolCallMessageComponent : MessageComponent
{
    public required string FunctionName { get; set; }
}

public class UnknownMessageComponent : MessageComponent
{
    public required string TagName { get; set; }
    public required string Content { get; set; }
}

public class SymptomCreatedMessageComponent : MessageComponent
{
    public required string RawJson { get; set; }
    public int? SymptomId { get; set; }
    public int? EpisodeId { get; set; }
    public string? SymptomName { get; set; }
}
