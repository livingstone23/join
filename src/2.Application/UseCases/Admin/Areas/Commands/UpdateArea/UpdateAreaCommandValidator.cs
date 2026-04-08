using FluentValidation;

namespace JOIN.Application.UseCases.Admin.Areas.Commands;

/// <summary>
/// Validates the payload used to update a tenant-scoped area.
/// </summary>
public sealed class UpdateAreaCommandValidator : AbstractValidator<UpdateAreaCommand>
{
    public UpdateAreaCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");

        RuleFor(x => x.CompanyId)
            .NotEmpty()
            .WithMessage("CompanyId is required.");

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(100)
            .WithMessage("Area name is required and must not exceed 100 characters.");

        RuleFor(x => x.EntityStatusId)
            .NotEmpty()
            .WithMessage("EntityStatusId is required.");
    }
}
