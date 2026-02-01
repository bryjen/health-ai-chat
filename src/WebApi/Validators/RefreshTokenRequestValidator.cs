using FluentValidation;
using Web.Common.DTOs.Auth;

namespace WebApi.Validators;

public class RefreshTokenRequestValidator 
    : AbstractValidator<RefreshTokenRequest>
{
    public RefreshTokenRequestValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("Refresh token is required");
    }
}
