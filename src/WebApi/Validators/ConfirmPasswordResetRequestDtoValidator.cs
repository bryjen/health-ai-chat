using FluentValidation;
using System.Text.RegularExpressions;
using WebApi.Controllers;
using WebApi.Controllers.Core;

namespace WebApi.Validators;

public class ConfirmPasswordResetRequestDtoValidator 
    : AbstractValidator<AuthController.ConfirmPasswordResetRequestDto>
{
    public ConfirmPasswordResetRequestDtoValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Token is required");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("New password is required")
            .MinimumLength(12).WithMessage("Password must be at least 12 characters long")
            .MaximumLength(100).WithMessage("Password must not exceed 100 characters")
            .Must(ContainUppercase).WithMessage("Password must contain at least one uppercase letter")
            .Must(ContainLowercase).WithMessage("Password must contain at least one lowercase letter")
            .Must(ContainDigit).WithMessage("Password must contain at least one number")
            .Must(ContainSpecialCharacter).WithMessage("Password must contain at least one special character (!@#$%^&*()_+-=[]{}|;':\",./<>?)")
            .Must(NotBeCommonPassword).WithMessage("Password is too common. Please choose a more unique password");
    }

    private static bool ContainUppercase(string password) 
        => Regex.IsMatch(password, @"[A-Z]");

    private static bool ContainLowercase(string password)
        => Regex.IsMatch(password, @"[a-z]");

    private static bool ContainDigit(string password)
        => Regex.IsMatch(password, @"[0-9]");

    private static bool ContainSpecialCharacter(string password)
        => Regex.IsMatch(password, @"[!@#$%^&*()_+\-=\[\]{};':""\\|,.<>\/?]");

    private static bool NotBeCommonPassword(string password)
    {
        var commonPasswords = new[] { "Password123!", "Password1!", "Admin123!", "Welcome123!" };
        return !commonPasswords.Contains(password, StringComparer.OrdinalIgnoreCase);
    }
}
