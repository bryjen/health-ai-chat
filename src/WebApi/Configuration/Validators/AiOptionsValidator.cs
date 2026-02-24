using FluentValidation;
using WebApi.Configuration.Options;

namespace WebApi.Configuration.Validators;

/// <summary>
/// Validates AI provider configuration options using FluentValidation.
/// Validates nested Microsoft Foundry and Anthropic options.
/// </summary>
public class AiOptionsValidator : AbstractValidator<AiOptions>
{
    public AiOptionsValidator()
    {
        // Validate Microsoft Foundry options using its validator
        RuleFor(x => x.MicrosoftFoundry)
            .SetValidator(new MicrosoftFoundryOptionsValidator())
            .When(x => !string.IsNullOrWhiteSpace(x.MicrosoftFoundry.Endpoint) ||
                       !string.IsNullOrWhiteSpace(x.MicrosoftFoundry.Key) ||
                       !string.IsNullOrWhiteSpace(x.MicrosoftFoundry.ModelDeploymentName));

        // Validate Anthropic options using its validator
        RuleFor(x => x.Anthropic)
            .SetValidator(new AnthropicOptionsValidator())
            .When(x => !string.IsNullOrWhiteSpace(x.Anthropic.ApiKey) ||
                       !string.IsNullOrWhiteSpace(x.Anthropic.DefaultModel));
    }
}
