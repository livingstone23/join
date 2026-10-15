using FluentValidation;

namespace JOIN.Application.UseCases.Customers.Customers.Commands;

/// <summary>
/// Validates <see cref="ActivateCustomerCommand"/>.
/// </summary>
public sealed class ActivateCustomerCommandValidator : AbstractValidator<ActivateCustomerCommand>
{
    public ActivateCustomerCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Customer id is required.");
        RuleFor(x => x.Notes).MaximumLength(500).WithMessage("Notes must not exceed 500 characters.");
    }
}

/// <summary>
/// Validates <see cref="DeactivateCustomerCommand"/>.
/// </summary>
public sealed class DeactivateCustomerCommandValidator : AbstractValidator<DeactivateCustomerCommand>
{
    public DeactivateCustomerCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Customer id is required.");
        RuleFor(x => x.Notes).MaximumLength(500).WithMessage("Notes must not exceed 500 characters.");
    }
}
