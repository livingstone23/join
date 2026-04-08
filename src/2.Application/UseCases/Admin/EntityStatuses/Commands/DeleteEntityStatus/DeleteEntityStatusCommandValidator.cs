using FluentValidation;

namespace JOIN.Application.UseCases.Admin.EntityStatuses.Commands;

/// <summary>
/// Validates the payload used to delete a tenant-scoped entity status.
/// </summary>
public sealed class DeleteEntityStatusCommandValidator : AbstractValidator<DeleteEntityStatusCommand>
{
    public DeleteEntityStatusCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");

        RuleFor(x => x.CompanyId)
            .NotEmpty()
            .WithMessage("CompanyId is required.");
    }
}
