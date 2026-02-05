using System.Diagnostics;
using System.Security.Claims;
using System.Text;

namespace WebApi.Middleware;

/// <summary>
/// Middleware to log HTTP requests and responses with timing, correlation IDs, and context.
/// </summary>
public class RequestLoggingMiddleware(
    RequestDelegate next,
    ILogger<RequestLoggingMiddleware> logger)
{
    private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Cookie",
        "X-API-Key",
        "X-Auth-Token"
    };

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var correlationId = GetOrCreateCorrelationId(context);

        // Add correlation ID to response headers for client tracing
        context.Response.Headers["X-Correlation-Id"] = correlationId;

        // Log request
        LogRequest(context, correlationId);

        // Capture original response body stream
        var originalBodyStream = context.Response.Body;
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        try
        {
            await next(context);
        }
        finally
        {
            stopwatch.Stop();

            // Log response
            LogResponse(context, correlationId, stopwatch.ElapsedMilliseconds, responseBody);

            // Copy response body back to original stream
            // This will include any error response written by GlobalExceptionHandlerMiddleware
            responseBody.Seek(0, SeekOrigin.Begin);
            await responseBody.CopyToAsync(originalBodyStream);
        }
    }

    private static string GetOrCreateCorrelationId(HttpContext context)
    {
        // Check if correlation ID exists in request headers
        if (context.Request.Headers.TryGetValue("X-Correlation-Id", out var correlationId) &&
            !string.IsNullOrWhiteSpace(correlationId))
        {
            return correlationId.ToString();
        }

        // Generate new correlation ID
        return Guid.NewGuid().ToString("N")[..16]; // Short format for readability
    }

    private void LogRequest(HttpContext context, string correlationId)
    {
        var request = context.Request;
        var userId = GetUserId(context);
        var ipAddress = GetClientIpAddress(context);

        var logData = new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["Method"] = request.Method,
            ["Path"] = request.Path.Value,
            ["QueryString"] = request.QueryString.HasValue ? request.QueryString.Value : null,
            ["IP"] = ipAddress,
            ["UserId"] = userId ?? "(anonymous)",
            ["UserAgent"] = request.Headers["User-Agent"].ToString(),
            ["RequestSize"] = request.ContentLength ?? 0,
            ["Headers"] = GetSafeHeaders(request.Headers)
        };

        // Use Debug level for requests to reduce verbosity (errors/warnings will still be logged)
        logger.LogInformation(
            "HTTP Request: {Method} {Path} | CorrelationId: {CorrelationId} | IP: {IP} | UserId: {UserId}",
            request.Method,
            request.Path.Value,
            correlationId,
            ipAddress,
            userId ?? "(anonymous)");
    }

    private void LogResponse(HttpContext context, string correlationId, long durationMs, MemoryStream responseBody)
    {
        var response = context.Response;
        var userId = GetUserId(context);
        var statusCode = response.StatusCode;
        var logLevel = GetLogLevel(statusCode);

        var responseSize = responseBody.Length;
        var responseBodyText = string.Empty;

        // Only capture response body for errors (and limit size)
        if (statusCode >= 400 && responseSize < 5000)
        {
            responseBody.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(responseBody, Encoding.UTF8, leaveOpen: true);
            responseBodyText = reader.ReadToEnd();
            responseBody.Seek(0, SeekOrigin.Begin);
        }

        var logData = new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["StatusCode"] = statusCode,
            ["Duration"] = durationMs,
            ["UserId"] = userId ?? "(anonymous)",
            ["ResponseSize"] = responseSize
        };

        if (!string.IsNullOrEmpty(responseBodyText))
        {
            logData["ResponseBody"] = responseBodyText;
        }

        // Use Debug level for successful requests (2xx, 3xx) to reduce verbosity
        var responseLogLevel = statusCode < 400 ? LogLevel.Debug : logLevel;

        logger.Log(
            responseLogLevel,
            "HTTP Response: {StatusCode} | Duration: {Duration}ms | CorrelationId: {CorrelationId} | UserId: {UserId} | Size: {ResponseSize} bytes",
            statusCode,
            durationMs,
            correlationId,
            userId ?? "(anonymous)",
            responseSize);

        // Log response body for errors
        if (!string.IsNullOrEmpty(responseBodyText))
        {
            logger.LogWarning(
                "Response body for error: {ResponseBody} | CorrelationId: {CorrelationId}",
                responseBodyText,
                correlationId);
        }
    }

    private static string? GetUserId(HttpContext context)
    {
        var userIdClaim = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return userIdClaim;
    }

    private static string GetClientIpAddress(HttpContext context)
    {
        // Check for forwarded IP (when behind proxy/load balancer)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            // Take first IP if multiple
            return forwardedFor.Split(',')[0].Trim();
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static Dictionary<string, string> GetSafeHeaders(IHeaderDictionary headers)
    {
        var safeHeaders = new Dictionary<string, string>();

        foreach (var header in headers)
        {
            if (SensitiveHeaders.Contains(header.Key))
            {
                safeHeaders[header.Key] = "[REDACTED]";
            }
            else
            {
                safeHeaders[header.Key] = string.Join(", ", header.Value.ToArray());
            }
        }

        return safeHeaders;
    }

    private static LogLevel GetLogLevel(int statusCode)
    {
        return statusCode switch
        {
            >= 500 => LogLevel.Error,
            >= 400 => LogLevel.Warning,
            _ => LogLevel.Information
        };
    }
}
