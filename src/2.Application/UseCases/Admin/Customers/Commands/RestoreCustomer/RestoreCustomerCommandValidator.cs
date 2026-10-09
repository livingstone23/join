using FluentValidation;

namespace JOIN.Application.UseCases.Admin.Customers.Commands;

/// <summary>
/// Validates <see cref="RestoreCustomerCommand"/>.
/// </summary>
public sealed class RestoreCustomerCommandValidator : AbstractValidator<RestoreCustomerCommand>
{
    public RestoreCustomerCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty()
            .WithMessage("Id is required.");
    }
}
