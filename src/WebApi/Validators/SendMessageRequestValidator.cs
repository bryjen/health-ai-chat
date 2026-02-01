using FluentValidation;
using Web.Common.DTOs.Conversations;

namespace WebApi.Validators;

public class SendMessageRequestValidator 
    : AbstractValidator<SendMessageRequest>
{
    public SendMessageRequestValidator()
    {
        RuleFor(x => x.Content)
            .NotEmpty().WithMessage("Content is required")
            .MaximumLength(10000).WithMessage("Content must not exceed 10000 characters");

        RuleFor(x => x.ConversationId)
            .NotEqual(Guid.Empty).WithMessage("ConversationId must be a valid GUID")
            .When(x => x.ConversationId.HasValue);
    }
}
