using FluentValidation;

namespace JOIN.Application.UseCases.Admin.Customers.Commands;

/// <summary>
/// Defines validation rules for <see cref="DeleteCustomerCommand"/>.
/// </summary>
public class DeleteCustomerCommandValidator : AbstractValidator<DeleteCustomerCommand>
{
    public DeleteCustomerCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Customer id is required.");
    }
}
