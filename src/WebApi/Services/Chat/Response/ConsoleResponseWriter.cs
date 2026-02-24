using Spectre.Console;
using WebApi.Services.Chat.Formatters;

namespace WebApi.Services.Chat.Response;

/// <summary>
/// Console-specific implementation that applies ANSI colors and formatting.
/// </summary>
public class ConsoleResponseWriter(MessageFormatter formatter) : ResponseWriter(formatter)
{
    protected override Task WriteRawAsync(string text, CancellationToken cancellationToken)
    {
        System.Console.Write(text);
        System.Console.Out.Flush();
        return Task.CompletedTask;
    }

    protected override Task CompleteCoreAsync(CancellationToken cancellationToken)
    {
        System.Console.Out.Flush();
        return Task.CompletedTask;
    }

    protected override Task WriteCoreAsync(FormattedContent content, CancellationToken cancellationToken)
    {
        switch (content.Category)
        {
            case ContentCategory.Reasoning:
                AnsiConsole.Markup($"[red]{content.Text}[/]");
                break;

            case ContentCategory.ToolCall:
                AnsiConsole.Markup($"[blue]🔧 Calling: {content.Text}[/]");
                break;

            case ContentCategory.Text:
                System.Console.Write(content.Text);
                break;

            case ContentCategory.Usage:
                var msg = Markup.Escape($"\n[Usage] {content.Text}");
                AnsiConsole.MarkupLine($"[Aqua]{msg}[/]");
                break;

            case ContentCategory.ToolResult:
                // Skip tool results in streaming display
                break;
        }

        System.Console.Out.Flush();
        return Task.CompletedTask;
    }
}