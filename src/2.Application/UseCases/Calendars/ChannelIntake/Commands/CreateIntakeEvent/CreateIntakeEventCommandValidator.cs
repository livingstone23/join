using FluentValidation;
using JOIN.Domain.Calendars.Enums;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake.Commands.CreateIntakeEvent;

/// <summary>
/// Validates <see cref="CreateIntakeEventCommand"/> (SPEC 45): the field rules of the SPEC 44 activities plus the
/// requester, the channel and the message id, which make the request traceable and idempotent.
/// </summary>
public sealed class CreateIntakeEventCommandValidator : AbstractValidator<CreateIntakeEventCommand>
{
    public CreateIntakeEventCommandValidator()
    {
        RuleFor(x => x.ActivityCode).NotEmpty().WithMessage("activityCode is required.").MaximumLength(50);
        RuleFor(x => x.Title).NotEmpty().WithMessage("title is required.").MaximumLength(200);
        RuleFor(x => x.Summary).MaximumLength(500);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.Location).MaximumLength(300);
        RuleFor(x => x.VideoCallUrl).MaximumLength(500);
        RuleFor(x => x.RecurrenceRule).MaximumLength(500);
        RuleFor(x => x.Start).NotNull().When(x => !x.IsAllDay).WithMessage("start is required.");
        RuleFor(x => x.StartDate).NotNull().When(x => x.IsAllDay).WithMessage("startDate is required for an all-day activity.");
        RuleFor(x => x.TargetUserEmail).MaximumLength(256);
        RuleFor(x => x.RequesterPersonId).NotEmpty().WithMessage("requesterPersonId is required.");
        RuleFor(x => x.OriginChannelCode).NotEmpty().WithMessage("originChannelCode is required.");
        RuleFor(x => x.OriginType)
            .Must(value => value is null
                || (Enum.TryParse<CalendarEventOriginType>(value, true, out var parsed)
                    && parsed is CalendarEventOriginType.Phone or CalendarEventOriginType.Email))
            .WithMessage("originType must be Phone or Email.");
        RuleFor(x => x.OriginIdentifier).NotEmpty().WithMessage("originIdentifier is required.").MaximumLength(200);
        RuleFor(x => x.OriginReference).NotEmpty().WithMessage("originReference is required.").MaximumLength(200);
    }
}
