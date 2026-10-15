using FluentValidation;

namespace JOIN.Application.UseCases.Customers.Customers.Commands;

/// <summary>
/// Validates <see cref="UpdateCustomerCommand"/>.
/// </summary>
public sealed class UpdateCustomerCommandValidator : AbstractValidator<UpdateCustomerCommand>
{
    public UpdateCustomerCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Customer id is required.");

        RuleFor(x => x.CustomerStageId)
            .NotEmpty().WithMessage("Customer stage id is required.");
    }
}
