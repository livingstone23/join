using FluentValidation;

namespace JOIN.Application.UseCases.Common.CommunicationChannels.Commands;

/// <summary>
/// Validates <see cref="RestoreCommunicationChannelCommand"/>.
/// </summary>
public sealed class RestoreCommunicationChannelCommandValidator : AbstractValidator<RestoreCommunicationChannelCommand>
{
    public RestoreCommunicationChannelCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
