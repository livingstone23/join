using FluentValidation;

namespace JOIN.Application.UseCases.Admin.Industries.Commands;

/// <summary>
/// Validates the payload used to delete a tenant-scoped industry.
/// </summary>
public sealed class DeleteIndustryCommandValidator : AbstractValidator<DeleteIndustryCommand>
{
    public DeleteIndustryCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
