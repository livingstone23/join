using FluentValidation;
using JOIN.Domain.Catalog;
using JOIN.Domain.Enums;

namespace JOIN.Application.UseCases.Catalog.ProductAttachmentSettings.Commands;

/// <summary>Validates <see cref="CreateProductAttachmentSettingsCommand"/>.</summary>
public sealed class CreateProductAttachmentSettingsCommandValidator : AbstractValidator<CreateProductAttachmentSettingsCommand>
{
    public CreateProductAttachmentSettingsCommandValidator() => ProductAttachmentSettingsValidation.Apply(this);
}

/// <summary>Validates <see cref="UpdateProductAttachmentSettingsCommand"/>.</summary>
public sealed class UpdateProductAttachmentSettingsCommandValidator : AbstractValidator<UpdateProductAttachmentSettingsCommand>
{
    public UpdateProductAttachmentSettingsCommandValidator() => ProductAttachmentSettingsValidation.Apply(this);
}

/// <summary>Rules shared by both validators: known flags only, positive sizes and 1–5 images (rule 1).</summary>
internal static class ProductAttachmentSettingsValidation
{
    private const long MaxFileSizeBytes = 100L * 1024 * 1024;
    private const int AllDocumentTypes = (int)(DocumentType.Pdf | DocumentType.Word | DocumentType.Excel | DocumentType.Text | DocumentType.Image | DocumentType.Other);
    private const int AllImageFormats = (int)(ProductImageFormat.Jpeg | ProductImageFormat.Png | ProductImageFormat.Webp);

    internal static void Apply<T>(AbstractValidator<T> validator) where T : IProductAttachmentSettingsInput
    {
        validator.RuleFor(x => x.AllowedDocumentTypes)
            .Must(v => v > 0 && (v & ~AllDocumentTypes) == 0).WithMessage("AllowedDocumentTypes must combine known document types.");
        validator.RuleFor(x => x.MaxDocumentSizeBytes).InclusiveBetween(1, MaxFileSizeBytes);
        validator.RuleFor(x => x.MaxDocumentsPerProduct).InclusiveBetween(1, 1000);
        validator.RuleFor(x => x.AllowedImageFormats)
            .Must(v => v > 0 && (v & ~AllImageFormats) == 0).WithMessage("AllowedImageFormats must combine known image formats.");
        validator.RuleFor(x => x.MaxImageSizeBytes).InclusiveBetween(1, MaxFileSizeBytes);
        validator.RuleFor(x => x.MaxImagesPerProduct).InclusiveBetween(1, ProductImage.MaxImagesPerProduct);
    }
}
