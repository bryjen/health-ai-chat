namespace WebApi.ApiWrapper.Exceptions;

/// <summary>
/// Exception thrown when API returns a 401 Unauthorized response.
/// </summary>
public class ApiUnauthorizedException : ApiException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ApiUnauthorizedException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">Optional error code from the API.</param>
    public ApiUnauthorizedException(string message, string? errorCode = null)
        : base(message, 401, errorCode)
    {
    }
}
