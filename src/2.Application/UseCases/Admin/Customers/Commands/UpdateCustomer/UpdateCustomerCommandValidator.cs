using FluentValidation;
using JOIN.Domain.Enums;

namespace JOIN.Application.UseCases.Admin.Customers.Commands;

/// <summary>
/// Validates <see cref="UpdateCustomerCommand"/>.
/// </summary>
public sealed class UpdateCustomerCommandValidator : AbstractValidator<UpdateCustomerCommand>
{
    public UpdateCustomerCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Customer id is required.");

        RuleFor(x => x.PersonLifecycleStage)
            .IsInEnum().WithMessage("Person lifecycle stage must be a valid value.")
            .Must(stage => stage is PersonLifecycleStage.Lead
                or PersonLifecycleStage.Prospect
                or PersonLifecycleStage.Customer
                or PersonLifecycleStage.FormerCustomer)
            .WithMessage("Person lifecycle stage must be Lead, Prospect, Customer, or FormerCustomer.");
    }
}
