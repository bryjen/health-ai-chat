using FluentValidation;
using WebApi.Configuration.Options;

namespace WebApi.Configuration.Validators;

/// <summary>
/// Validates rate limiting settings configuration using FluentValidation
/// </summary>
public class RateLimitingSettingsValidator : AbstractValidator<RateLimitingSettings>
{
    public RateLimitingSettingsValidator()
    {
        // Validate Global policy
        RuleFor(x => x.Global)
            .SetValidator(new RateLimitPolicyValidator("Global"));

        // Validate Auth policy
        RuleFor(x => x.Auth)
            .SetValidator(new RateLimitPolicyValidator("Auth"));

        // Validate Authenticated policy
        RuleFor(x => x.Authenticated)
            .SetValidator(new RateLimitPolicyValidator("Authenticated"));

        // Ensure Auth policy is stricter than Global
        RuleFor(x => x.Auth.PermitLimit)
            .LessThan(x => x.Global.PermitLimit)
            .WithMessage("Auth rate limit (PermitLimit) should be stricter than Global limit");

        // Ensure Authenticated policy allows more than Global
        RuleFor(x => x.Authenticated.PermitLimit)
            .GreaterThan(x => x.Global.PermitLimit)
            .WithMessage("Authenticated rate limit (PermitLimit) should be higher than Global limit");
    }
}

/// <summary>
/// Validates individual rate limit policy settings
/// </summary>
/// <remarks>
/// This validator is internal because it's only used as a nested validator and requires a constructor parameter
/// that cannot be resolved by dependency injection. Making it internal prevents FluentValidation from
/// auto-discovering and attempting to register it as a service.
/// </remarks>
internal class RateLimitPolicyValidator : AbstractValidator<RateLimitPolicy>
{
    public RateLimitPolicyValidator(string policyName)
    {
        RuleFor(x => x.PermitLimit)
            .GreaterThanOrEqualTo(1).WithMessage($"{policyName} rate limit PermitLimit must be at least 1")
            .LessThanOrEqualTo(10000).WithMessage($"{policyName} rate limit PermitLimit should not exceed 10000");

        RuleFor(x => x.WindowMinutes)
            .GreaterThanOrEqualTo(1).WithMessage($"{policyName} rate limit WindowMinutes must be at least 1")
            .LessThanOrEqualTo(60).WithMessage($"{policyName} rate limit WindowMinutes should not exceed 60");

        RuleFor(x => x.QueueLimit)
            .GreaterThanOrEqualTo(0).WithMessage($"{policyName} rate limit QueueLimit must be 0 or greater");
    }
}
