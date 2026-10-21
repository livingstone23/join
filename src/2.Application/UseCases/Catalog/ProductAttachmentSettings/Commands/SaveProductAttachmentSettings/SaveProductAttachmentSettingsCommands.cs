using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;

namespace JOIN.Application.UseCases.Catalog.ProductAttachmentSettings.Commands;

/// <summary>Fields shared by the create and update commands of the product attachment limits.</summary>
public interface IProductAttachmentSettingsInput
{
    int AllowedDocumentTypes { get; }
    long MaxDocumentSizeBytes { get; }
    int MaxDocumentsPerProduct { get; }
    int AllowedImageFormats { get; }
    long MaxImageSizeBytes { get; }
    int MaxImagesPerProduct { get; }
}

/// <summary><c>POST /ProductAttachmentSettings</c> (SPEC 70): creates the company's row; 409 when it already exists.</summary>
public sealed record CreateProductAttachmentSettingsCommand : IProductAttachmentSettingsInput, ITransactionalCommand<Response<ProductAttachmentSettingsDto>>
{
    public int AllowedDocumentTypes { get; init; }
    public long MaxDocumentSizeBytes { get; init; }
    public int MaxDocumentsPerProduct { get; init; }
    public int AllowedImageFormats { get; init; }
    public long MaxImageSizeBytes { get; init; }
    public int MaxImagesPerProduct { get; init; }
}

/// <summary><c>PUT /ProductAttachmentSettings</c> (SPEC 70): updates the company's row.</summary>
public sealed record UpdateProductAttachmentSettingsCommand : IProductAttachmentSettingsInput, ITransactionalCommand<Response<ProductAttachmentSettingsDto>>
{
    public int AllowedDocumentTypes { get; init; }
    public long MaxDocumentSizeBytes { get; init; }
    public int MaxDocumentsPerProduct { get; init; }
    public int AllowedImageFormats { get; init; }
    public long MaxImageSizeBytes { get; init; }
    public int MaxImagesPerProduct { get; init; }
}
