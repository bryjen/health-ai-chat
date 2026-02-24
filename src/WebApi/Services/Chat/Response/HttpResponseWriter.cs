using System.Text.Json;
using WebApi.Services.Chat.Formatters;

namespace WebApi.Services.Chat.Response;

/// <summary>
/// HTTP streaming implementation of ResponseWriter.
/// Writes NDJSON lines to the HTTP response body.
/// </summary>
public class HttpResponseWriter(MessageFormatter formatter, IHttpContextAccessor httpContextAccessor)
    : ResponseWriter(formatter)
{
    private HttpResponse Response => httpContextAccessor.HttpContext!.Response;

    protected override async Task WriteRawAsync(string text, CancellationToken cancellationToken)
    {
        await Response.WriteAsync(text, cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    protected override async Task WriteCoreAsync(FormattedContent content, CancellationToken cancellationToken)
    {
        var type = content.Category switch
        {
            ContentCategory.Text => "text",
            ContentCategory.Reasoning => "reasoning",
            ContentCategory.Usage => "usage",
            _ => "unknown"
        };

        var escaped = JsonSerializer.Serialize(content.Text);
        await WriteRawAsync($"{{\"type\":\"{type}\",\"content\":{escaped}}}\n", cancellationToken);
    }

    protected override async Task CompleteCoreAsync(CancellationToken cancellationToken)
    {
        await Response.Body.FlushAsync(cancellationToken);
    }
}
