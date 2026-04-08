using FluentValidation;

namespace JOIN.Application.UseCases.Admin.Projects.Commands;

/// <summary>
/// Validates the payload used to update a tenant-scoped project.
/// </summary>
public sealed class UpdateProjectCommandValidator : AbstractValidator<UpdateProjectCommand>
{
    public UpdateProjectCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");

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