using FluentValidation;
using JOIN.Domain.Catalog;

namespace JOIN.Application.UseCases.Catalog.Products.Images.Commands;

/// <summary>Validates <see cref="UploadProductImageCommand"/>; the limits of the company are checked by the handler.</summary>
public sealed class UploadProductImageCommandValidator : AbstractValidator<UploadProductImageCommand>
{
    public UploadProductImageCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.OriginalName).NotEmpty().MaximumLength(ProductImage.MaxNameLength);
        RuleFor(x => x.ContentType).NotEmpty().MaximumLength(ProductImage.MaxContentTypeLength);
        RuleFor(x => x.Content).NotNull().Must(c => c is { Length: > 0 }).WithMessage("The image file is empty.");
    }
}

/// <summary>Validates <see cref="ReorderProductImagesCommand"/>.</summary>
public sealed class ReorderProductImagesCommandValidator : AbstractValidator<ReorderProductImagesCommand>
{
    public ReorderProductImagesCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.ImageIds).NotNull()
            .Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("ImageIds must not contain duplicates.");
        RuleForEach(x => x.ImageIds).NotEmpty();
    }
}

/// <summary>Validates <see cref="SetPrimaryProductImageCommand"/>.</summary>
public sealed class SetPrimaryProductImageCommandValidator : AbstractValidator<SetPrimaryProductImageCommand>
{
    public SetPrimaryProductImageCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.ImageId).NotEmpty();
    }
}

/// <summary>Validates <see cref="DeleteProductImageCommand"/>.</summary>
public sealed class DeleteProductImageCommandValidator : AbstractValidator<DeleteProductImageCommand>
{
    public DeleteProductImageCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.ImageId).NotEmpty();
    }
}

/// <summary>Validates <see cref="RestoreProductImageCommand"/>.</summary>
public sealed class RestoreProductImageCommandValidator : AbstractValidator<RestoreProductImageCommand>
{
    public RestoreProductImageCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.ImageId).NotEmpty();
    }
}
