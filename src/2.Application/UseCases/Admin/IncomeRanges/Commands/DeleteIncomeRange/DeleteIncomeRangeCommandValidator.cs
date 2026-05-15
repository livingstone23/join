using FluentValidation;

namespace JOIN.Application.UseCases.Admin.IncomeRanges.Commands;

public sealed class DeleteIncomeRangeCommandValidator : AbstractValidator<DeleteIncomeRangeCommand>
{
    public DeleteIncomeRangeCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}
