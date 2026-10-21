using FluentValidation;
using JOIN.Domain.Catalog;

namespace JOIN.Application.UseCases.Catalog.ProductDocumentTypes.Commands;

/// <summary>Validates <see cref="CreateProductDocumentTypeCommand"/>.</summary>
public sealed class CreateProductDocumentTypeCommandValidator : AbstractValidator<CreateProductDocumentTypeCommand>
{
    public CreateProductDocumentTypeCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(ProductDocumentType.MaxCodeLength);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(ProductDocumentType.MaxNameLength);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
    }
}

/// <summary>Validates <see cref="UpdateProductDocumentTypeCommand"/>.</summary>
public sealed class UpdateProductDocumentTypeCommandValidator : AbstractValidator<UpdateProductDocumentTypeCommand>
{
    public UpdateProductDocumentTypeCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(ProductDocumentType.MaxCodeLength);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(ProductDocumentType.MaxNameLength);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
    }
}

/// <summary>Validates <see cref="DeleteProductDocumentTypeCommand"/>.</summary>
public sealed class DeleteProductDocumentTypeCommandValidator : AbstractValidator<DeleteProductDocumentTypeCommand>
{
    public DeleteProductDocumentTypeCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

/// <summary>Validates <see cref="RestoreProductDocumentTypeCommand"/>.</summary>
public sealed class RestoreProductDocumentTypeCommandValidator : AbstractValidator<RestoreProductDocumentTypeCommand>
{
    public RestoreProductDocumentTypeCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}
