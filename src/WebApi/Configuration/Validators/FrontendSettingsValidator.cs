using FluentValidation;
using WebApi.Configuration.Options;

namespace WebApi.Configuration.Validators;

/// <summary>
/// Validates frontend settings configuration using FluentValidation
/// </summary>
public class FrontendSettingsValidator : AbstractValidator<FrontendSettings>
{
    public FrontendSettingsValidator(IHostEnvironment environment)
    {
        if (environment.IsProduction())
        {
            RuleFor(x => x.BaseUrl)
                .NotEmpty().WithMessage("Frontend BaseUrl is required in production");
        }

        // Validate URL format if provided
        RuleFor(x => x.BaseUrl)
            .Must(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                        (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
                         uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)))
            .WithMessage("Frontend BaseUrl must be a valid absolute URL using http or https scheme")
            .When(x => !string.IsNullOrWhiteSpace(x.BaseUrl));
    }
}
