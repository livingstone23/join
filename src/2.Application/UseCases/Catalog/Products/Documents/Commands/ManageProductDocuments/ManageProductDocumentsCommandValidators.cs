using FluentValidation;
using JOIN.Domain.Catalog;

namespace JOIN.Application.UseCases.Catalog.Products.Documents.Commands;

/// <summary>Validates <see cref="UploadProductDocumentCommand"/>; the limits of the company are checked by the handler.</summary>
public sealed class UploadProductDocumentCommandValidator : AbstractValidator<UploadProductDocumentCommand>
{
    public UploadProductDocumentCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.ProductDocumentTypeId).NotEmpty();
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(ProductDocument.MaxDisplayNameLength);
        RuleFor(x => x.Description).MaximumLength(ProductDocument.MaxDescriptionLength);
        RuleFor(x => x.OriginalName).NotEmpty().MaximumLength(ProductDocument.MaxNameLength);
        RuleFor(x => x.ContentType).NotEmpty().MaximumLength(ProductDocument.MaxContentTypeLength);
        RuleFor(x => x.Content).NotNull().Must(c => c is { Length: > 0 }).WithMessage("The document file is empty.");
    }
}

/// <summary>Validates <see cref="DeleteProductDocumentCommand"/>.</summary>
public sealed class DeleteProductDocumentCommandValidator : AbstractValidator<DeleteProductDocumentCommand>
{
    public DeleteProductDocumentCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.DocumentId).NotEmpty();
    }
}

/// <summary>Validates <see cref="RestoreProductDocumentCommand"/>.</summary>
public sealed class RestoreProductDocumentCommandValidator : AbstractValidator<RestoreProductDocumentCommand>
{
    public RestoreProductDocumentCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.DocumentId).NotEmpty();
    }
}
