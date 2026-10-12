using FluentValidation;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake;

/// <summary>
/// Validates <see cref="CreateIntakeEventCommand"/> (SPEC 45 F4). Same all-day logic as SPEC 44;
/// <c>RequesterPersonId</c> and <c>OriginReference</c> are mandatory for the agent's request to be auditable.
/// </summary>
public sealed class CreateIntakeEventCommandValidator : AbstractValidator<CreateIntakeEventCommand>
{
    public CreateIntakeEventCommandValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty().WithMessage("The X-Company-Id header is required.");
        RuleFor(x => x.ActivityCode).NotEmpty().WithMessage("The activity code is required.");
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200).WithMessage("Title is required (max 200 characters).");
        RuleFor(x => x.Summary).MaximumLength(500).When(x => x.Summary is not null)
            .WithMessage("Summary cannot exceed 500 characters.");
        RuleFor(x => x.Description).MaximumLength(4000).When(x => x.Description is not null)
            .WithMessage("Description cannot exceed 4000 characters.");
        RuleFor(x => x.Location).MaximumLength(300).When(x => x.Location is not null)
            .WithMessage("Location cannot exceed 300 characters.");
        RuleFor(x => x.VideoCallUrl)
            .MaximumLength(500).When(x => x.VideoCallUrl is not null)
            .WithMessage("Video call URL cannot exceed 500 characters.");

        RuleFor(x => x.StartDate).NotNull().When(x => x.IsAllDay)
            .WithMessage("startDate is required for an all-day activity.");
        RuleFor(x => x.Start).NotNull().When(x => !x.IsAllDay)
            .WithMessage("start is required.");

        RuleFor(x => x.RecurrenceRule).MaximumLength(500).When(x => x.RecurrenceRule is not null)
            .WithMessage("Recurrence rule cannot exceed 500 characters.");

        RuleFor(x => x.RequesterPersonId).NotEmpty().WithMessage("RequesterPersonId is required.");
        RuleFor(x => x.OriginChannelCode).NotEmpty().WithMessage("Origin channel code is required.");
        RuleFor(x => x.OriginIdentifier).NotEmpty().MaximumLength(200)
            .WithMessage("Origin identifier is required (max 200 characters).");
        RuleFor(x => x.OriginReference).NotEmpty().MaximumLength(200)
            .WithMessage("Origin reference is required (max 200 characters).");
    }
}