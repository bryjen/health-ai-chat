using System.Diagnostics.CodeAnalysis;

namespace WebApi.Configuration.Options;

/// <summary>
/// Rate limiting configuration settings
/// </summary>
[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")]
[SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
public class RateLimitingSettings
{
    public const string SectionName = "RateLimiting";
    
    public RateLimitPolicy Global { get; set; } = new();
    public RateLimitPolicy Auth { get; set; } = new();
    public RateLimitPolicy Authenticated { get; set; } = new();
}

[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")]
[SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
public class RateLimitPolicy
{
    public int PermitLimit { get; set; } = 100;
    public int WindowMinutes { get; set; } = 1;
    public int QueueLimit { get; set; } = 10;
}
