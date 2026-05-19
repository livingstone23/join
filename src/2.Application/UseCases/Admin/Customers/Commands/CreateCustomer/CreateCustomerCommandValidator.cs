using FluentValidation;
using JOIN.Domain.Enums;



namespace JOIN.Application.UseCases.Admin.Customers.Commands;



/// <summary>
/// Validates <see cref="CreateCustomerCommand"/>.
/// </summary>
public sealed class CreateCustomerCommandValidator : AbstractValidator<CreateCustomerCommand>
{
    public CreateCustomerCommandValidator()
    {
        RuleFor(x => x.PersonId)
            .NotEmpty().WithMessage("Person id is required.");

        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User id is required.");

        RuleFor(x => x.PersonLifecycleStage)
            .IsInEnum().WithMessage("Person lifecycle stage must be a valid value.")
            .Must(stage => stage is PersonLifecycleStage.Lead
                or PersonLifecycleStage.Prospect
                or PersonLifecycleStage.Customer
                or PersonLifecycleStage.FormerCustomer)
            .WithMessage("Person lifecycle stage must be Lead, Prospect, Customer, or FormerCustomer.");
    }
}
