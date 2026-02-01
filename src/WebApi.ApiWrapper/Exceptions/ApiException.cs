namespace WebApi.ApiWrapper.Exceptions;

/// <summary>
/// Base exception for API client errors.
/// </summary>
public class ApiException : Exception
{
    /// <summary>
    /// HTTP status code returned by the API.
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// Optional error code from the API response.
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Optional additional details from the API response.
    /// </summary>
    public object? Details { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="errorCode">Optional error code from the API.</param>
    /// <param name="details">Optional additional details.</param>
    public ApiException(string message, int statusCode, string? errorCode = null, object? details = null)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        Details = details;
    }

    /// <summary>
    /// Gets a message that describes the current exception, including status code and error code if available.
    /// </summary>
    public override string Message
    {
        get
        {
            var baseMessage = base.Message;
            if (!string.IsNullOrWhiteSpace(ErrorCode))
            {
                return $"[{StatusCode}] {ErrorCode}: {baseMessage}";
            }
            return $"[{StatusCode}] {baseMessage}";
        }
    }
}
