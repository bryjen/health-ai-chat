using System.Diagnostics.CodeAnalysis;

namespace WebApi.Configuration.Options;

/// <summary>
/// Request size limit settings
/// </summary>
[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
public class RequestLimitsSettings
{
    public const string SectionName = "RequestLimits";
    
    /// <summary>
    /// Maximum request body size in bytes (default: 10 MB)
    /// </summary>
    public long MaxRequestBodySizeBytes { get; set; } = 10 * 1024 * 1024; // 10 MB
    
    /// <summary>
    /// Maximum form value length in bytes (default: 4 MB)
    /// </summary>
    public int MaxFormValueLength { get; set; } = 4 * 1024 * 1024; // 4 MB
    
    /// <summary>
    /// Maximum form key length in bytes (default: 2 KB)
    /// </summary>
    public int MaxFormKeyLength { get; set; } = 2 * 1024; // 2 KB
    
    /// <summary>
    /// Maximum form file size in bytes (default: 5 MB)
    /// </summary>
    public long MaxFormFileSizeBytes { get; set; } = 5 * 1024 * 1024; // 5 MB
}
