using FluentValidation;

namespace JOIN.Application.UseCases.Admin.IncomeRanges.Commands;

public sealed class CreateIncomeRangeCommandValidator : AbstractValidator<CreateIncomeRangeCommand>
{
    public CreateIncomeRangeCommandValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DisplayOrder).GreaterThanOrEqualTo(1);
        RuleFor(x => x.CurrencyCode).NotEmpty().MaximumLength(3);
        RuleFor(x => x.MaximumValue)
            .Must((cmd, max) => !max.HasValue || max.Value >= cmd.MinimumValue)
            .WithMessage("MaximumValue must be greater than or equal to MinimumValue.");
    }
}
