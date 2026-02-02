using System.Diagnostics.CodeAnalysis;

namespace WebApi.Configuration.Options;

/// <summary>
/// Application version settings following semantic versioning (SemVer) format.
/// </summary>
/// <remarks>
/// Semantic versioning format: MAJOR.MINOR.PATCH[-PRERELEASE][+BUILDMETADATA]
/// - MAJOR: Incremented for incompatible API changes
/// - MINOR: Incremented for backwards-compatible functionality additions
/// - PATCH: Incremented for backwards-compatible bug fixes
/// - PRERELEASE: Optional pre-release identifier (e.g., "alpha", "beta", "rc.1")
/// - BUILDMETADATA: Optional build metadata (e.g., "build.123", "sha.abc123")
/// </remarks>
[SuppressMessage("ReSharper", "PropertyCanBeMadeInitOnly.Global")]
[SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
public class VersionSettings
{
    public const string SectionName = "Version";
    
    /// <summary>
    /// Major version number (incremented for incompatible API changes).
    /// </summary>
    public int Major { get; set; } = 1;
    
    /// <summary>
    /// Minor version number (incremented for backwards-compatible functionality additions).
    /// </summary>
    public int Minor { get; set; } = 0;
    
    /// <summary>
    /// Patch version number (incremented for backwards-compatible bug fixes).
    /// </summary>
    public int Patch { get; set; } = 0;
    
    /// <summary>
    /// Optional pre-release identifier (e.g., "alpha", "beta", "rc.1").
    /// </summary>
    public string? PreRelease { get; set; }
    
    /// <summary>
    /// Optional build metadata (e.g., "build.123", "sha.abc123").
    /// </summary>
    public string? BuildMetadata { get; set; }
    
    /// <summary>
    /// Gets the full semantic version string in the format: MAJOR.MINOR.PATCH[-PRERELEASE][+BUILDMETADATA]
    /// </summary>
    public string ToSemanticVersion()
    {
        var version = $"{Major}.{Minor}.{Patch}";
        
        if (!string.IsNullOrWhiteSpace(PreRelease))
        {
            version += $"-{PreRelease}";
        }
        
        if (!string.IsNullOrWhiteSpace(BuildMetadata))
        {
            version += $"+{BuildMetadata}";
        }
        
        return version;
    }
}
