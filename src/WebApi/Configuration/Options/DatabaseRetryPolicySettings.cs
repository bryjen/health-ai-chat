using System.Diagnostics.CodeAnalysis;

namespace WebApi.Configuration.Options;

/// <summary>
/// Database connection retry policy settings
/// </summary>
[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")]
[SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
public class DatabaseRetryPolicySettings
{
    public const string SectionName = "Database:RetryPolicy";
    
    /// <summary>
    /// Maximum number of retry attempts for transient failures
    /// </summary>
    public int MaxRetryCount { get; set; } = 5;
    
    /// <summary>
    /// Maximum delay between retries in seconds
    /// </summary>
    public int MaxRetryDelaySeconds { get; set; } = 30;
    
    /// <summary>
    /// Additional error codes to treat as transient (comma-separated)
    /// If null, uses default PostgreSQL transient error codes
    /// </summary>
    public string? ErrorCodesToAdd { get; set; }
}
