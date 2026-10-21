using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Catalog;
using JOIN.Domain.Catalog;
using MediatR;

namespace JOIN.Application.UseCases.Catalog.ProductDocumentTypes.Commands;

/// <summary>Handles <see cref="CreateProductDocumentTypeCommand"/>: the normalized code is unique among the company's live types.</summary>
public sealed class CreateProductDocumentTypeCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<CreateProductDocumentTypeCommand, Response<ProductDocumentTypeDto>>
{
    public async Task<Response<ProductDocumentTypeDto>> Handle(CreateProductDocumentTypeCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return ProductDocumentTypeRules.CompanyRequired<ProductDocumentTypeDto>();
        }

        var companyId = currentUserService.CompanyId;
        if (await ProductDocumentTypeRules.CodeTakenAsync(unitOfWork, companyId, request.Code, exceptId: null))
        {
            return ProductDocumentTypeRules.AlreadyExists();
        }

        var entity = new ProductDocumentType { CompanyId = companyId };
        ProductDocumentTypeRules.Apply(entity, request);
        await unitOfWork.GetRepository<ProductDocumentType>().InsertAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<ProductDocumentTypeDto>
        {
            IsSuccess = true,
            Message = "Product document type created successfully.",
            Data = ProductDocumentTypeRules.ToDto(entity)
        };
    }
}

/// <summary>Handles <see cref="UpdateProductDocumentTypeCommand"/>.</summary>
public sealed class UpdateProductDocumentTypeCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<UpdateProductDocumentTypeCommand, Response<ProductDocumentTypeDto>>
{
    public async Task<Response<ProductDocumentTypeDto>> Handle(UpdateProductDocumentTypeCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return ProductDocumentTypeRules.CompanyRequired<ProductDocumentTypeDto>();
        }

        var companyId = currentUserService.CompanyId;
        var repository = unitOfWork.GetRepository<ProductDocumentType>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != companyId || entity.GcRecord != 0)
        {
            return ProductDocumentTypeRules.NotFound<ProductDocumentTypeDto>();
        }

        if (await ProductDocumentTypeRules.CodeTakenAsync(unitOfWork, companyId, request.Code, exceptId: request.Id))
        {
            return ProductDocumentTypeRules.AlreadyExists();
        }

        ProductDocumentTypeRules.Apply(entity, request);
        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<ProductDocumentTypeDto>
        {
            IsSuccess = true,
            Message = "Product document type updated successfully.",
            Data = ProductDocumentTypeRules.ToDto(entity)
        };
    }
}

/// <summary>Handles <see cref="DeleteProductDocumentTypeCommand"/>: blocked while live documents reference the type.</summary>
public sealed class DeleteProductDocumentTypeCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteProductDocumentTypeCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(DeleteProductDocumentTypeCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return ProductDocumentTypeRules.CompanyRequired<Guid>();
        }

        var repository = unitOfWork.GetRepository<ProductDocumentType>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != currentUserService.CompanyId || entity.GcRecord != 0)
        {
            return ProductDocumentTypeRules.NotFound<Guid>();
        }

        var check = await new ActiveDependentsCheck(unitOfWork).CountAsync<ProductDocument>(
            x => x.GcRecord == 0 && x.ProductDocumentTypeId == entity.Id,
            "ProductDocuments");
        if (check.HasDependents)
        {
            return Response<Guid>.Error(CatalogErrorCodes.ProductDocumentTypeInUse, [.. check.Details]);
        }

        entity.MarkAsDeleted();
        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<Guid> { IsSuccess = true, Message = "Product document type deleted successfully.", Data = entity.Id };
    }
}

/// <summary>Handles <see cref="RestoreProductDocumentTypeCommand"/> with <see cref="SoftDeleteRestorer"/>.</summary>
public sealed class RestoreProductDocumentTypeCommandHandler(
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer)
    : IRequestHandler<RestoreProductDocumentTypeCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(RestoreProductDocumentTypeCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return ProductDocumentTypeRules.CompanyRequired<Guid>();
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error(CatalogErrorCodes.SuperAdminRequired, ["Only a SuperAdmin can restore deleted records."]);
        }

        return await restorer.RestoreAsync<ProductDocumentType>(
            request.Id,
            request.CompanyId,
            activeDuplicateExists: (entity, _) => restorer.AnyAsync<ProductDocumentType>(
                x => x.GcRecord == 0 && x.Id != entity.Id && x.CompanyId == entity.CompanyId && x.Code == entity.Code),
            cancellationToken: cancellationToken);
    }
}

/// <summary>Rules shared by the document type commands.</summary>
internal static class ProductDocumentTypeRules
{
    internal static void Apply(ProductDocumentType entity, IProductDocumentTypeInput input)
    {
        entity.Code = ProductDocumentType.NormalizeCode(input.Code);
        entity.Name = input.Name.Trim();
        entity.SortOrder = input.SortOrder;
        entity.IsActive = input.IsActive;
    }

    internal static async Task<bool> CodeTakenAsync(IUnitOfWork unitOfWork, Guid companyId, string code, Guid? exceptId)
    {
        var normalized = ProductDocumentType.NormalizeCode(code);
        return (await unitOfWork.GetRepository<ProductDocumentType>().GetAllIncludingDeletedAsync(
            x => x.CompanyId == companyId && x.Code == normalized && x.GcRecord == 0 && x.Id != exceptId)).Any();
    }

    internal static ProductDocumentTypeDto ToDto(ProductDocumentType s) => new()
    {
        Id = s.Id,
        CompanyId = s.CompanyId,
        Code = s.Code,
        Name = s.Name,
        SortOrder = s.SortOrder,
        IsActive = s.IsActive,
        CreatedAt = s.Created
    };

    internal static Response<T> CompanyRequired<T>() =>
        Response<T>.Error(CatalogErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);

    internal static Response<T> NotFound<T>() =>
        Response<T>.Error(CatalogErrorCodes.ProductDocumentTypeNotFound, ["Product document type not found."]);

    internal static Response<ProductDocumentTypeDto> AlreadyExists() =>
        Response<ProductDocumentTypeDto>.Error(
            CatalogErrorCodes.ProductDocumentTypeAlreadyExists,
            ["An active product document type with that code already exists."]);
}
