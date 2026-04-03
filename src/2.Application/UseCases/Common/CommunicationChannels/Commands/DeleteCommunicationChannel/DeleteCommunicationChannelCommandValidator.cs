using FluentValidation;

namespace JOIN.Application.UseCases.Common.CommunicationChannels.Commands;

/// <summary>
/// Defines validation rules for <see cref="DeleteCommunicationChannelCommand"/>.
/// </summary>
public class DeleteCommunicationChannelCommandValidator : AbstractValidator<DeleteCommunicationChannelCommand>
{
    public DeleteCommunicationChannelCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Communication channel id is required.");
    }
}
