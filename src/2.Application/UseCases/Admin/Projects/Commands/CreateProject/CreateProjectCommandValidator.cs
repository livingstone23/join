using FluentValidation;

namespace JOIN.Application.UseCases.Admin.Projects.Commands;

/// <summary>
/// Validates the payload used to create a tenant-scoped project.
/// </summary>
public sealed class CreateProjectCommandValidator : AbstractValidator<CreateProjectCommand>
{
    public CreateProjectCommandValidator()
    {
        RuleFor(x => x.CompanyId)
            .NotEmpty()
            .WithMessage("CompanyId is required.");

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(150)
            .WithMessage("Project name is required and must not exceed 150 characters.");

        RuleFor(x => x.EntityStatusId)
            .NotEmpty()
            .WithMessage("EntityStatusId is required.");
    }
}