using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;

namespace JOIN.Application.UseCases.Catalog.ProductDocumentTypes.Commands;

/// <summary>Fields shared by the create and update commands of a product document type.</summary>
public interface IProductDocumentTypeInput
{
    string Code { get; }
    string Name { get; }
    int SortOrder { get; }
    bool IsActive { get; }
}

/// <summary><c>POST /ProductDocumentTypes</c> (SPEC 70): adds a classification to the company's catalog.</summary>
public sealed record CreateProductDocumentTypeCommand : IProductDocumentTypeInput, ITransactionalCommand<Response<ProductDocumentTypeDto>>
{
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
}

/// <summary><c>PUT /ProductDocumentTypes/{id}</c> (SPEC 70).</summary>
public sealed record UpdateProductDocumentTypeCommand : IProductDocumentTypeInput, ITransactionalCommand<Response<ProductDocumentTypeDto>>
{
    public Guid Id { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool IsActive { get; init; } = true;
}

/// <summary><c>DELETE /ProductDocumentTypes/{id}</c> (SPEC 70): blocked by <c>PRODUCT_DOCUMENT_TYPE_IN_USE</c> while live documents use it.</summary>
public sealed record DeleteProductDocumentTypeCommand(Guid Id) : ITransactionalCommand<Response<Guid>>;

/// <summary><c>POST /ProductDocumentTypes/{id}/restore</c> (SPEC 70): SuperAdmin only.</summary>
public sealed record RestoreProductDocumentTypeCommand(Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;
