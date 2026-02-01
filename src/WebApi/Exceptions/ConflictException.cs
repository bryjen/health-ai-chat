namespace WebApi.Exceptions;

/// <summary>
/// Exception thrown when a resource conflict occurs (e.g., duplicate username)
/// </summary>
public class ConflictException(
    string message) 
    : Exception(message);
