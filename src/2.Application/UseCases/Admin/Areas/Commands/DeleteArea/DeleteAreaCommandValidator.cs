using FluentValidation;

namespace JOIN.Application.UseCases.Admin.Areas.Commands;

/// <summary>
/// Validates the payload used to delete a tenant-scoped area.
/// </summary>
public sealed class DeleteAreaCommandValidator : AbstractValidator<DeleteAreaCommand>
{
    public DeleteAreaCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");

        RuleFor(x => x.CompanyId)
            .NotEmpty()
            .WithMessage("CompanyId is required.");
    }
}
