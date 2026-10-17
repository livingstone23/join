using FluentValidation;

namespace JOIN.Application.UseCases.Catalog.ProductCategories.Commands;

/// <summary>
/// Rules shared by the product category commands.
/// </summary>
internal static class ProductCategoryInputRules
{
    internal static void Apply<T>(AbstractValidator<T> validator) where T : IProductCategoryInput
    {
        validator.RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        validator.RuleFor(x => x.ParentId)
            .Must(id => id is null || id != Guid.Empty)
            .WithMessage("The parent category identifier is invalid.");
    }
}

/// <summary>
/// Validates <see cref="CreateProductCategoryCommand"/>.
/// </summary>
public sealed class CreateProductCategoryCommandValidator : AbstractValidator<CreateProductCategoryCommand>
{
    public CreateProductCategoryCommandValidator() => ProductCategoryInputRules.Apply(this);
}

/// <summary>
/// Validates <see cref="UpdateProductCategoryCommand"/>.
/// </summary>
public sealed class UpdateProductCategoryCommandValidator : AbstractValidator<UpdateProductCategoryCommand>
{
    public UpdateProductCategoryCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        ProductCategoryInputRules.Apply(this);
    }
}
