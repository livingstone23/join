using FluentValidation;

namespace JOIN.Application.UseCases.Common.CommunicationChannels.Commands;

/// <summary>
/// Defines validation rules for <see cref="CreateCommunicationChannelCommand"/>.
/// </summary>
public class CreateCommunicationChannelCommandValidator : AbstractValidator<CreateCommunicationChannelCommand>
{
    public CreateCommunicationChannelCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Channel name is required.")
            .MaximumLength(100).WithMessage("Channel name cannot exceed 100 characters.");

        RuleFor(x => x.Provider)
            .MaximumLength(100).WithMessage("Provider cannot exceed 100 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Provider));

        RuleFor(x => x.Code)
            .MaximumLength(50).WithMessage("Code cannot exceed 50 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.Code));
    }
}
