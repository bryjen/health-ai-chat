using FluentValidation;
using WebApi.Controllers;
using WebApi.Controllers.Core;

namespace WebApi.Validators;

public class PasswordResetRequestDtoValidator 
    : AbstractValidator<AuthController.PasswordResetRequestDto>
{
    public PasswordResetRequestDtoValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("Invalid email address");
    }
}
