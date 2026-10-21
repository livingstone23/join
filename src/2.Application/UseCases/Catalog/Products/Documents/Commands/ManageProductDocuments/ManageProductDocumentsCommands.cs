using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;

namespace JOIN.Application.UseCases.Catalog.Products.Documents.Commands;

/// <summary><c>POST /Products/{id}/documents</c> (SPEC 70): multipart upload with its type, display name and description.</summary>
public sealed record UploadProductDocumentCommand(
    Guid ProductId,
    Guid ProductDocumentTypeId,
    string DisplayName,
    string? Description,
    string OriginalName,
    string ContentType,
    byte[] Content) : ITransactionalCommand<Response<ProductDocumentDto>>;

/// <summary><c>DELETE /Products/{id}/documents/{docId}</c> (SPEC 70): logical delete; the file stays in storage.</summary>
public sealed record DeleteProductDocumentCommand(Guid ProductId, Guid DocumentId) : ITransactionalCommand<Response<Guid>>;

/// <summary><c>POST /Products/{id}/documents/{docId}/restore</c> (SPEC 70): SuperAdmin only.</summary>
public sealed record RestoreProductDocumentCommand(Guid ProductId, Guid DocumentId) : ITransactionalCommand<Response<Guid>>;
