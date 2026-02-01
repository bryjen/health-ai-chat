using FluentValidation;
using Microsoft.Extensions.Options;

namespace WebApi.Configuration.Validators;

/// <summary>
/// Adapter that bridges FluentValidation validators with IValidateOptions for Options pattern integration
/// </summary>
public class FluentValidationOptionsAdapter<T>(
    IValidator<T> validator) 
    : IValidateOptions<T>
    where T : class
{
    public ValidateOptionsResult Validate(string? name, T options)
    {
        var validationResult = validator.Validate(options);
        
        if (validationResult.IsValid)
        {
            return ValidateOptionsResult.Success;
        }

        var errors = validationResult.Errors
            .Select(error => error.ErrorMessage)
            .ToList();

        return ValidateOptionsResult.Fail(errors);
    }
}
