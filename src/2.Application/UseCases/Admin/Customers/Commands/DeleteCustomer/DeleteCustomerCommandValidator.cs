using FluentValidation;



namespace JOIN.Application.UseCases.Admin.Customers.Commands;



/// <summary>
/// Validates <see cref="DeleteCustomerCommand"/>.
/// </summary>
public sealed class DeleteCustomerCommandValidator : AbstractValidator<DeleteCustomerCommand>
{
    public DeleteCustomerCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Customer id is required.");
    }
}
