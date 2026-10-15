using FluentValidation;

namespace JOIN.Application.UseCases.Customers.Customers.Commands;

/// <summary>
/// Validates <see cref="CreateCustomerCommand"/>.
/// </summary>
public sealed class CreateCustomerCommandValidator : AbstractValidator<CreateCustomerCommand>
{
    public CreateCustomerCommandValidator()
    {
        RuleFor(x => x.PersonId)
            .NotEmpty().WithMessage("Person id is required.");

        RuleFor(x => x.CustomerStageId)
            .NotEqual(Guid.Empty).When(x => x.CustomerStageId.HasValue)
            .WithMessage("Customer stage id must not be empty.");
    }
}
