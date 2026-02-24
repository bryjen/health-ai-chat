namespace WebApi.Configuration.Options;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable UnusedMember.Global

#region Request Limits & Rate Limiting
    /// Request size limit settings.
    public class RequestLimitsSettings
    {
        public const string SectionName = "RequestLimits";

        /// Maximum request body size in bytes (default: 10 MB)
        public long MaxRequestBodySizeBytes { get; set; } = 10 * 1024 * 1024; // 10 MB

        /// Maximum form value length in bytes (default: 4 MB)
        public int MaxFormValueLength { get; set; } = 4 * 1024 * 1024; // 4 MB

        /// Maximum form key length in bytes (default: 2 KB)
        public int MaxFormKeyLength { get; set; } = 2 * 1024; // 2 KB

        /// Maximum form file size in bytes (default: 5 MB)
        public long MaxFormFileSizeBytes { get; set; } = 5 * 1024 * 1024; // 5 MB
    }

    /// Rate limiting configuration settings>
    public class RateLimitingSettings
    {
        public const string SectionName = "RateLimiting";

        public RateLimitPolicy Global { get; set; } = new();
        public RateLimitPolicy Auth { get; set; } = new();
        public RateLimitPolicy Authenticated { get; set; } = new();
    }

    /// Rate limiting policy.
    public class RateLimitPolicy
    {
        public int PermitLimit { get; set; } = 100;
        public int WindowMinutes { get; set; } = 1;
        public int QueueLimit { get; set; } = 10;
    }
#endregion

#region Auth
    /// JWT authentication settings.
    public class JwtSettings
    {
        public const string SectionName = "Jwt";

        public string Secret { get; set; } = string.Empty;
        public string Issuer { get; set; } = string.Empty;
        public string Audience { get; set; } = string.Empty;
        public int AccessTokenExpirationMinutes { get; set; } = 120;
        public int RefreshTokenExpirationDays { get; set; } = 30;
    }

    /// OAuth provider settings.
    public class OAuthSettings
    {
        public const string SectionName = "OAuth";

        public GoogleOAuthSettings Google { get; set; } = new();
        public MicrosoftOAuthSettings Microsoft { get; set; } = new();
        public GitHubOAuthSettings GitHub { get; set; } = new();
    }

    /// Google OAuth settings.
    public class GoogleOAuthSettings
    {
        public string ClientId { get; set; } = string.Empty;
    }

    /// Microsoft OAuth settings.
    public class MicrosoftOAuthSettings
    {
        public string ClientId { get; set; } = string.Empty;
        public string TenantId { get; set; } = "common";
    }

    /// GitHub OAuth settings.
    public class GitHubOAuthSettings
    {
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
    }
#endregion

#region Other
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

    /// <summary>
    /// Email service settings (Resend)
    /// </summary>
    public class EmailSettings
    {
        public const string SectionName = "Email:Resend";

        public string ApiKey { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
    }

    /// <summary>
    /// Frontend application settings
    /// </summary>
    public class FrontendSettings
    {
        public const string SectionName = "Frontend";

        public string BaseUrl { get; set; } = string.Empty;
    }
#endregion

