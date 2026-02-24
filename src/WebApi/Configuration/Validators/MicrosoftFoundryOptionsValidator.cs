using FluentValidation;
using WebApi.Configuration.Options;

namespace WebApi.Configuration.Validators;

/// <summary>
/// Validates Microsoft Foundry (Azure OpenAI) configuration settings using FluentValidation.
/// </summary>
public class MicrosoftFoundryOptionsValidator : AbstractValidator<MicrosoftFoundryOptions>
{
    public MicrosoftFoundryOptionsValidator()
    {
        // Validate Endpoint - must be a valid absolute URI
        RuleFor(x => x.Endpoint)
            .NotEmpty()
            .WithMessage("Microsoft Foundry Endpoint is required. Please configure 'AiOptions:MicrosoftFoundry:Endpoint' in appsettings.json")
            .Must(uri => Uri.TryCreate(uri, UriKind.Absolute, out _))
            .WithMessage("Microsoft Foundry Endpoint must be a valid absolute URI");

        // Validate Key - required
        RuleFor(x => x.Key)
            .NotEmpty()
            .WithMessage("Microsoft Foundry Key is required. Please configure 'AiOptions:MicrosoftFoundry:Key' in appsettings.json");

        // Validate ModelDeploymentName - required
        RuleFor(x => x.ModelDeploymentName)
            .NotEmpty()
            .WithMessage("Microsoft Foundry ModelDeploymentName is required. Please configure 'AiOptions:MicrosoftFoundry:ModelDeploymentName' in appsettings.json");
    }
}
