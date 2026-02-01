namespace WebApi.Exceptions;

/// <summary>
/// Exception thrown when validation fails
/// </summary>
public class ValidationException(
    string message) 
    : Exception(message);
