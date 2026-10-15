using FluentValidation;

namespace JOIN.Application.UseCases.Customers.CustomerStages.Commands;

/// <summary>
/// Rules shared by the create and update validators.
/// </summary>
internal static class CustomerStageInputRules
{
    internal static void Apply<T>(AbstractValidator<T> validator) where T : ICustomerStageInput
    {
        validator.RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(20)
            .WithMessage("Customer stage code is required and must not exceed 20 characters.");

        validator.RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(100)
            .WithMessage("Customer stage name is required and must not exceed 100 characters.");

        validator.RuleFor(x => x.Description)
            .MaximumLength(250)
            .WithMessage("Customer stage description must not exceed 250 characters.");

        validator.RuleFor(x => x.SortOrder)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Sort order must be zero or greater.");
    }
}

/// <summary>
/// Validates <see cref="CreateCustomerStageCommand"/>.
/// </summary>
public sealed class CreateCustomerStageCommandValidator : AbstractValidator<CreateCustomerStageCommand>
{
    public CreateCustomerStageCommandValidator() => CustomerStageInputRules.Apply(this);
}

/// <summary>
/// Validates <see cref="UpdateCustomerStageCommand"/>.
/// </summary>
public sealed class UpdateCustomerStageCommandValidator : AbstractValidator<UpdateCustomerStageCommand>
{
    public UpdateCustomerStageCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Id is required.");
        CustomerStageInputRules.Apply(this);
    }
}

/// <summary>
/// Validates <see cref="DeleteCustomerStageCommand"/>.
/// </summary>
public sealed class DeleteCustomerStageCommandValidator : AbstractValidator<DeleteCustomerStageCommand>
{
    public DeleteCustomerStageCommandValidator() => RuleFor(x => x.Id).NotEmpty().WithMessage("Id is required.");
}

/// <summary>
/// Validates <see cref="RestoreCustomerStageCommand"/>.
/// </summary>
public sealed class RestoreCustomerStageCommandValidator : AbstractValidator<RestoreCustomerStageCommand>
{
    public RestoreCustomerStageCommandValidator() => RuleFor(x => x.Id).NotEmpty().WithMessage("Id is required.");
}
