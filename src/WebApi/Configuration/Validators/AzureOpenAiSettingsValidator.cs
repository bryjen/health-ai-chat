using FluentValidation;
using WebApi.Configuration.Options;

namespace WebApi.Configuration.Validators;

/// <summary>
/// Validates Azure OpenAI settings configuration using FluentValidation.
/// </summary>
public class AzureOpenAiSettingsValidator : AbstractValidator<AzureOpenAiSettings>
{
    public AzureOpenAiSettingsValidator(IHostEnvironment _)
    {
        // Azure OpenAI settings are required in all environments; misconfiguration should fail fast.
        RuleFor(x => x.Endpoint)
            .NotEmpty().WithMessage("AzureOpenAI Endpoint is required");

        RuleFor(x => x.ApiKey)
            .NotEmpty().WithMessage("AzureOpenAI ApiKey is required");

        RuleFor(x => x.DeploymentName)
            .NotEmpty().WithMessage("AzureOpenAI DeploymentName is required");
    }
}
