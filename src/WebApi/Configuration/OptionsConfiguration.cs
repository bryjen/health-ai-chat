using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.Extensions.Options;
using WebApi.Configuration.Options;
using WebApi.Configuration.Validators;
using WebApi.Validators;

namespace WebApi.Configuration;

/// <summary>
/// Separate static class for configuring application options as well as validation properties that allow for quick
/// failure (to ensure that app parameters are valid on startup).
/// </summary>
public static class OptionsConfiguration
{
    /// <summary>
    /// Configures all application options with FluentValidation-based validation.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">The configuration instance to read settings from.</param>
    /// <remarks>
    /// <para>
    /// This method performs the following operations in order:
    /// 1. Registers FluentValidation validators from both request validators and configuration validators assemblies
    /// 2. Registers all application settings (JWT, Email, Frontend, RateLimiting, OAuth) with validation
    /// 3. Enables automatic FluentValidation for ASP.NET Core model validation
    /// </para>
    /// <para>
    /// Settings are bound from configuration sections and validated using FluentValidation validators
    /// that implement <see cref="AbstractValidator{T}"/>. The validators are automatically discovered
    /// and registered first, then bridged to the Options pattern via <see cref="FluentValidationOptionsAdapter{T}"/>
    /// when settings are registered.
    /// </para>
    /// <para>
    /// Validation occurs at application startup (fail-fast) and can be checked using
    /// <see cref="IOptions{T}"/> or <see cref="IOptionsSnapshot{T}"/>.
    /// </para>
    /// </remarks>
    public static void ConfigureAppOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // register request validators (scoped - used during request processing)
        services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();

        // register configuration validators as Singleton (stateless, used at startup)
        // these need to be Singleton because IValidateOptions<T> is a singleton and resolves them
        services.AddSingleton<IValidator<JwtSettings>, JwtSettingsValidator>();
        services.AddSingleton<IValidator<EmailSettings>, EmailSettingsValidator>();
        services.AddSingleton<IValidator<FrontendSettings>, FrontendSettingsValidator>();
        services.AddSingleton<IValidator<RateLimitingSettings>, RateLimitingSettingsValidator>();
        services.AddSingleton<IValidator<AzureOpenAiSettings>, AzureOpenAiSettingsValidator>();

        services.AddValidatedSettings<JwtSettings>(configuration);
        services.AddValidatedSettings<EmailSettings>(configuration);
        services.AddValidatedSettings<FrontendSettings>(configuration);
        services.AddValidatedSettings<RateLimitingSettings>(configuration);
        services.AddValidatedSettings<OAuthSettings>(configuration, hasValidator: false);
        services.AddValidatedSettings<AzureOpenAiSettings>(configuration);
        services.AddValidatedSettings<VectorStoreSettings>(configuration, hasValidator: false);
        services.AddValidatedSettings<VersionSettings>(configuration, hasValidator: false);

        // enable automatic FluentValidation for ASP.NET Core model binding
        services.AddFluentValidationAutoValidation();
    }

    /// <summary>
    /// Registers a strongly-typed settings class with optional FluentValidation-based validation.
    /// </summary>
    /// <typeparam name="T">The settings class type. Must have a public static property named "SectionName" that returns the configuration section name.</typeparam>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">The configuration instance to read settings from.</param>
    /// <param name="hasValidator">Whether to register a FluentValidation validator for this settings type. Defaults to <c>true</c>.</param>
    /// <returns>The service collection for method chaining.</returns>
    /// <remarks>
    /// <para>
    /// This method:
    /// 1. Binds the settings class from the configuration section (determined by the static "SectionName" property or the class name)
    /// 2. Optionally registers a FluentValidation validator as an <see cref="IValidateOptions{T}"/> adapter
    /// </para>
    /// <para>
    /// The configuration section name is determined by:
    /// - First checking for a public static property named "SectionName" on the type
    /// - If not found, using the class name as the section name
    /// </para>
    /// <para>
    /// When <paramref name="hasValidator"/> is <c>true</c>, a FluentValidation validator must be registered
    /// (typically via <c>AddValidatorsFromAssemblyContaining</c> extension method from FluentValidation)
    /// before this method is called. The validator is automatically bridged to the Options pattern using
    /// <see cref="FluentValidationOptionsAdapter{T}"/>.
    /// </para>
    /// <para>
    /// Example usage:
    /// <code>
    /// // Settings class with SectionName property
    /// public class MySettings
    /// {
    ///     public const string SectionName = "MySection";
    ///     public string Value { get; set; }
    /// }
    /// 
    /// // FluentValidation validator
    /// public class MySettingsValidator : AbstractValidator&lt;MySettings&gt;
    /// {
    ///     public MySettingsValidator()
    ///     {
    ///         RuleFor(x => x.Value).NotEmpty();
    ///     }
    /// }
    /// 
    /// // Registration
    /// services.AddValidatorsFromAssemblyContaining&lt;MySettingsValidator&gt;();
    /// services.AddValidatedSettings&lt;MySettings&gt;(configuration);
    /// </code>
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="hasValidator"/> is <c>true</c> but no FluentValidation validator
    /// for type <typeparamref name="T"/> is registered in the service collection.
    /// </exception>
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    public static IServiceCollection AddValidatedSettings<T>(
        this IServiceCollection services,
        IConfiguration configuration,
        bool hasValidator = true) where T : class
    {
        var sectionName = typeof(T).GetField("SectionName",
            BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string ?? typeof(T).Name;

        services.Configure<T>(configuration.GetSection(sectionName));

        if (hasValidator)
        {
            services.AddSingleton<IValidateOptions<T>>(sp =>
                new FluentValidationOptionsAdapter<T>(sp.GetRequiredService<IValidator<T>>()));
        }

        return services;
    }
}