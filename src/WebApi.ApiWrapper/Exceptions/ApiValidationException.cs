namespace WebApi.ApiWrapper.Exceptions;

/// <summary>
/// Exception thrown when API returns a 400 Bad Request with validation errors.
/// </summary>
public class ApiValidationException : ApiException
{
    /// <summary>
    /// Dictionary of validation errors, keyed by field name.
    /// </summary>
    public Dictionary<string, string[]> Errors { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiValidationException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errors">Dictionary of validation errors.</param>
    /// <param name="errorCode">Optional error code from the API.</param>
    public ApiValidationException(string message, Dictionary<string, string[]>? errors = null, string? errorCode = null)
        : base(message, 400, errorCode)
    {
        Errors = errors ?? new Dictionary<string, string[]>();
    }
}
