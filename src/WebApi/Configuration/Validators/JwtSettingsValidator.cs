using System.Diagnostics.CodeAnalysis;
using FluentValidation;
using WebApi.Configuration.Options;

namespace WebApi.Configuration.Validators;

/// <summary>
/// Validates JWT settings configuration using FluentValidation
/// </summary>
[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
public class JwtSettingsValidator : AbstractValidator<JwtSettings>
{
    public JwtSettingsValidator(IHostEnvironment environment)
    {
        RuleFor(x => x.Secret)
            .NotEmpty().WithMessage("JWT Secret is required")
            .MinimumLength(environment.IsProduction() ? 32 : 16)
            .WithMessage(environment.IsProduction() 
                ? "JWT Secret must be at least 32 characters for security (production requirement)"
                : "JWT Secret must be at least 16 characters");

        RuleFor(x => x.Issuer)
            .NotEmpty().WithMessage("JWT Issuer is required");

        RuleFor(x => x.Audience)
            .NotEmpty().WithMessage("JWT Audience is required");

        RuleFor(x => x.AccessTokenExpirationMinutes)
            .GreaterThanOrEqualTo(1).WithMessage("AccessTokenExpirationMinutes must be at least 1")
            .LessThanOrEqualTo(1440).WithMessage("AccessTokenExpirationMinutes should not exceed 1440 (24 hours) for security");

        RuleFor(x => x.RefreshTokenExpirationDays)
            .GreaterThanOrEqualTo(1).WithMessage("RefreshTokenExpirationDays must be at least 1")
            .LessThanOrEqualTo(365).WithMessage("RefreshTokenExpirationDays should not exceed 365 days for security");
    }
}
