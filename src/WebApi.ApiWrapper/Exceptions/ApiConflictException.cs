namespace WebApi.ApiWrapper.Exceptions;

/// <summary>
/// Exception thrown when API returns a 409 Conflict response.
/// </summary>
public class ApiConflictException : ApiException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ApiConflictException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">Optional error code from the API.</param>
    public ApiConflictException(string message, string? errorCode = null)
        : base(message, 409, errorCode)
    {
    }
}
