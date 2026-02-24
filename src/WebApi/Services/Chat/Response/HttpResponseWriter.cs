using WebApi.Services.Chat.Formatters;

namespace WebApi.Services.Chat.Response;

/// <summary>
/// HTTP streaming implementation of ResponseWriter.
/// Mirrors ConsoleResponseWriter output exactly, writing to the HTTP response body instead of stdout.
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
        switch (content.Category)
        {
            case ContentCategory.ToolResult:
                break;

            default:
                await Response.WriteAsync(content.Text, cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
                break;
        }
    }

    protected override async Task CompleteCoreAsync(CancellationToken cancellationToken)
    {
        await Response.Body.FlushAsync(cancellationToken);
    }
}
