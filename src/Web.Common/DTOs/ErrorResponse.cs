namespace Web.Common.DTOs;

/// <summary>
/// Standard error response format for API errors
/// </summary>
public class ErrorResponse
{
    public string Message { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public Dictionary<string, string[]>? Errors { get; set; }
}