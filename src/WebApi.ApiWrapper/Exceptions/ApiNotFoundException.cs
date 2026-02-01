namespace WebApi.ApiWrapper.Exceptions;

/// <summary>
/// Exception thrown when API returns a 404 Not Found response.
/// </summary>
public class ApiNotFoundException : ApiException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ApiNotFoundException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="errorCode">Optional error code from the API.</param>
    public ApiNotFoundException(string message, string? errorCode = null)
        : base(message, 404, errorCode)
    {
    }
}
