using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using WebApi.Configuration.Options;

// Note: This must be in the root namespace (no namespace declaration) to be part of the same partial class as Program.cs

/// <summary>
/// Helper methods for Program.cs configuration validation
/// </summary>
[SuppressMessage("ReSharper", "CheckNamespace")]
public partial class Program
{
    /// <summary>
    /// Validates all configuration options on startup
    /// </summary>
    private static void ValidateConfigurationOnStartup(
        IServiceProvider services, 
        IHostEnvironment environment, 
        ILogger logger)
    {
        var validationErrors = new List<string>();
        var warnings = new List<string>();

        // Validate JWT settings
        ValidateOptions<JwtSettings>(services, "JWT settings", validationErrors, warnings, environment);

        // Validate Email settings
        ValidateOptions<EmailSettings>(services, "Email settings", validationErrors, warnings, environment);

        // Validate Frontend settings
        ValidateOptions<FrontendSettings>(services, "Frontend settings", validationErrors, warnings, environment);

        // Validate Rate Limiting settings
        ValidateOptions<RateLimitingSettings>(services, "Rate Limiting settings", validationErrors, warnings, environment);

        // Log warnings (non-blocking in development)
        foreach (var warning in warnings)
        {
            logger.LogWarning("Configuration warning: {Warning}", warning);
        }

        // Fail if there are validation errors
        if (validationErrors.Count > 0)
        {
            var errorMessage = "Configuration validation failed:\n" + string.Join("\n", validationErrors.Select(e => $"  - {e}"));
            logger.LogError("Configuration validation failed. Application will not start.");
            throw new InvalidOperationException(errorMessage);
        }

        if (warnings.Count > 0 && environment.IsProduction())
        {
            logger.LogWarning("Configuration warnings detected in production. Review configuration settings.");
        }
    }

    private static void ValidateOptions<T>(
        IServiceProvider services,
        string settingsName,
        List<string> errors,
        List<string> warnings,
        IHostEnvironment environment) where T : class
    {
        var options = services.GetRequiredService<IOptions<T>>();
        var validateOptions = services.GetService<IValidateOptions<T>>();

        if (validateOptions == null)
        {
            return; // No validator registered
        }

        var result = validateOptions.Validate(Options.DefaultName, options.Value);

        if (result.Failed)
        {
            foreach (var failure in result.Failures)
            {
                // In production, all validation failures are errors
                // In development, some might be warnings
                if (environment.IsProduction() || !IsOptionalInDevelopment<T>())
                {
                    errors.Add($"{settingsName}: {failure}");
                }
                else
                {
                    warnings.Add($"{settingsName}: {failure}");
                }
            }
        }
    }

    private static bool IsOptionalInDevelopment<T>() where T : class
    {
        // Email settings are optional in development
        return typeof(T) == typeof(EmailSettings);
    }
}
