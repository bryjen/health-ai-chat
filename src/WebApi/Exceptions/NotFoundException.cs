namespace WebApi.Exceptions;

/// <summary>
/// Exception thrown when a requested resource is not found
/// </summary>
public class NotFoundException(
    string message) 
    : Exception(message);
