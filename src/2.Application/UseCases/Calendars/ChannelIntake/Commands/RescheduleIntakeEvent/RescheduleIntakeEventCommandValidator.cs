using FluentValidation;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake.Commands.RescheduleIntakeEvent;

/// <summary>
/// Validates <see cref="RescheduleIntakeEventCommand"/> (SPEC 45).
/// </summary>
public sealed class RescheduleIntakeEventCommandValidator : AbstractValidator<RescheduleIntakeEventCommand>
{
    public RescheduleIntakeEventCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty().WithMessage("The activity id is required.");
        RuleFor(x => x.RequesterPersonId).NotEmpty().WithMessage("requesterPersonId is required.");
        RuleFor(x => x.Start).NotEqual(default(DateTimeOffset)).WithMessage("start is required.");
        RuleFor(x => x.End).GreaterThan(x => x.Start).When(x => x.End.HasValue).WithMessage("end must be later than start.");
        RuleFor(x => x.Scope).IsInEnum().When(x => x.Scope.HasValue);
        RuleFor(x => x.OriginChannelCode).NotEmpty().WithMessage("originChannelCode is required.");
        RuleFor(x => x.OriginIdentifier).NotEmpty().WithMessage("originIdentifier is required.").MaximumLength(200);
        RuleFor(x => x.OriginReference).NotEmpty().WithMessage("originReference is required.").MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}
