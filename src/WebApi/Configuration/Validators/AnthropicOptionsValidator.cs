using FluentValidation;
using WebApi.Configuration.Options;

namespace WebApi.Configuration.Validators;

/// <summary>
/// Validates Anthropic (Claude) API configuration settings using FluentValidation.
/// </summary>
public class AnthropicOptionsValidator : AbstractValidator<AnthropicOptions>
{
    public AnthropicOptionsValidator()
    {
        // Validate ApiKey - required
        RuleFor(x => x.ApiKey)
            .NotEmpty()
            .WithMessage("Anthropic ApiKey is required. Please configure 'AiOptions:Anthropic:ApiKey' in appsettings.json");
    }
}
