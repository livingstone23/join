using FluentValidation;

namespace JOIN.Application.UseCases.Admin.PersonEmployments.Commands;

/// <summary>
/// Validation rules for <see cref="CreatePersonEmploymentCommand"/>.
/// </summary>
public sealed class CreatePersonEmploymentValidator : AbstractValidator<CreatePersonEmploymentCommand>
{
    /// <summary>
    /// Initializes validator rules for creating person employment records.
    /// </summary>
    public CreatePersonEmploymentValidator()
    {
        RuleFor(x => x.PersonId)
            .NotEmpty().WithMessage("Person id is required.");

        RuleFor(x => x.EmployerName)
            .NotEmpty().WithMessage("Employer name is required.")
            .MaximumLength(200).WithMessage("Employer name cannot exceed 200 characters.");

        RuleFor(x => x.JobTitle)
            .NotEmpty().WithMessage("Job title is required.")
            .MaximumLength(150).WithMessage("Job title cannot exceed 150 characters.");

        RuleFor(x => x.StartDate)
            .NotEmpty().WithMessage("Start date is required.");

        RuleFor(x => x.EndDate)
            .NotEmpty()
            .When(x => x.IsCurrent == false)
            .WithMessage("End date is required when employment is not current.");

        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate)
            .When(x => x.EndDate.HasValue)
            .WithMessage("End date must be on or after the start date.");
    }
}
