using FluentValidation;
using JOIN.Domain.Catalog;

namespace JOIN.Application.UseCases.Catalog.Products.Commands;

/// <summary>
/// Rules shared by <see cref="CreateProductCommand"/> and <see cref="UpdateProductCommand"/>.
/// </summary>
internal static class ProductInputRules
{
    internal static void Apply<T>(AbstractValidator<T> validator) where T : IProductInput
    {
        validator.RuleFor(x => x.Code)
            .Must(code => !string.IsNullOrWhiteSpace(code))
            .WithMessage("Code is required.")
            .Must(code => Product.NormalizeCode(code ?? string.Empty).Length <= 30)
            .WithMessage("Code cannot exceed 30 characters.");

        validator.RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        validator.RuleFor(x => x.Description).MaximumLength(1000);
        validator.RuleFor(x => x.Kind)
            .Must(kind => Enum.IsDefined(typeof(ProductKind), kind))
            .WithMessage("Kind must be 1 (Product) or 2 (Service).");
        validator.RuleFor(x => x.ProductCategoryId)
            .Must(id => id is null || id != Guid.Empty)
            .WithMessage("The product category identifier is invalid.");
        validator.RuleFor(x => x.ReferencePrice)
            .GreaterThanOrEqualTo(0)
            .PrecisionScale(18, 2, ignoreTrailingZeros: true)
            .WithMessage("ReferencePrice must be zero or positive, with up to 2 decimals.");
    }
}

/// <summary>
/// Validates <see cref="CreateProductCommand"/>.
/// </summary>
public sealed class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator() => ProductInputRules.Apply(this);
}

/// <summary>
/// Validates <see cref="UpdateProductCommand"/>.
/// </summary>
public sealed class UpdateProductCommandValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        ProductInputRules.Apply(this);
    }
}
