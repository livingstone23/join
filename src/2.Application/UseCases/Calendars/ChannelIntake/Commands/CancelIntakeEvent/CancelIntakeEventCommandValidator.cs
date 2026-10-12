using FluentValidation;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake.Commands.CancelIntakeEvent;

/// <summary>
/// Validates <see cref="CancelIntakeEventCommand"/> (SPEC 45): cancelling always requires a reason.
/// </summary>
public sealed class CancelIntakeEventCommandValidator : AbstractValidator<CancelIntakeEventCommand>
{
    public CancelIntakeEventCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty().WithMessage("The activity id is required.");
        RuleFor(x => x.RequesterPersonId).NotEmpty().WithMessage("requesterPersonId is required.");
        RuleFor(x => x.Reason).NotEmpty().WithMessage("reason is required to cancel an activity.").MaximumLength(500);
        RuleFor(x => x.Scope).IsInEnum().When(x => x.Scope.HasValue);
        RuleFor(x => x.OriginChannelCode).NotEmpty().WithMessage("originChannelCode is required.");
        RuleFor(x => x.OriginIdentifier).NotEmpty().WithMessage("originIdentifier is required.").MaximumLength(200);
    }
}
