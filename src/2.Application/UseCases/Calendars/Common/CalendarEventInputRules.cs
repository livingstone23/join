using FluentValidation;

namespace JOIN.Application.UseCases.Calendars.Common;

/// <summary>
/// Shared FluentValidation rules of <see cref="ICalendarEventInput"/>.
/// </summary>
public static class CalendarEventInputRules
{
    public static void Apply<T>(AbstractValidator<T> validator)
        where T : ICalendarEventInput
    {
        validator.RuleFor(x => x.CalendarActivityCompanyId).NotEmpty().WithMessage("The activity type is required.");
        validator.RuleFor(x => x.Title).NotEmpty().WithMessage("The title is required.")
            .MaximumLength(200).WithMessage("The title cannot exceed 200 characters.");
        validator.RuleFor(x => x.Summary).MaximumLength(500).WithMessage("The summary cannot exceed 500 characters.");
        validator.RuleFor(x => x.Description).MaximumLength(4000).WithMessage("The description cannot exceed 4000 characters.");
        validator.RuleFor(x => x.Location).MaximumLength(300).WithMessage("The location cannot exceed 300 characters.");
        validator.RuleFor(x => x.VideoCallUrl)
            .MaximumLength(500).WithMessage("The video call URL cannot exceed 500 characters.")
            .Must(BeHttpsUrl).WithMessage("The video call URL must be a valid https URL.")
            .When(x => !string.IsNullOrWhiteSpace(x.VideoCallUrl));
        validator.RuleFor(x => x.StartDate).NotNull().When(x => x.IsAllDay).WithMessage("startDate is required for an all-day activity.");
        validator.RuleFor(x => x.Start).NotNull().When(x => !x.IsAllDay).WithMessage("start is required.");
        validator.RuleFor(x => x.RecurrenceRule).MaximumLength(500).WithMessage("The recurrence rule cannot exceed 500 characters.");
    }

    public static bool BeHttpsUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
