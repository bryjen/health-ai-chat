using FluentValidation;
using WebApi.Configuration.Options;

namespace WebApi.Configuration.Validators;

/// <summary>
/// Validates email service settings configuration using FluentValidation
/// </summary>
public class EmailSettingsValidator : AbstractValidator<EmailSettings>
{
    public EmailSettingsValidator(IHostEnvironment environment)
    {
        // In production, email settings are required
        // In development, they're optional (can use mock/disabled email)
        if (environment.IsProduction())
        {
            RuleFor(x => x.ApiKey)
                .NotEmpty().WithMessage("Email ApiKey is required in production");

            RuleFor(x => x.Domain)
                .NotEmpty().WithMessage("Email Domain is required in production");
        }

        // Validate domain format if provided
        RuleFor(x => x.Domain)
            .Must(domain => string.IsNullOrWhiteSpace(domain) || 
                           domain.Contains('.') || 
                           domain == "example.org")
            .WithMessage("Email Domain appears to be invalid (should be a valid domain name)")
            .When(x => !string.IsNullOrWhiteSpace(x.Domain));
    }
}
