using FluentValidation;

namespace JOIN.Application.UseCases.Admin.IncomeRanges.Commands;

/// <summary>
/// Validates <see cref="RestoreIncomeRangeCommand"/>.
/// </summary>
public sealed class RestoreIncomeRangeCommandValidator : AbstractValidator<RestoreIncomeRangeCommand>
{
    public RestoreIncomeRangeCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
