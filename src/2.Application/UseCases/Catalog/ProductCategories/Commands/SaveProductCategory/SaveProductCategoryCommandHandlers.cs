using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Catalog;
using JOIN.Domain.Catalog;
using MediatR;

namespace JOIN.Application.UseCases.Catalog.ProductCategories.Commands;

/// <summary>
/// Handles <see cref="CreateProductCategoryCommand"/>.
/// </summary>
public sealed class CreateProductCategoryCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<CreateProductCategoryCommand, Response<ProductCategoryDto>>
{
    public async Task<Response<ProductCategoryDto>> Handle(CreateProductCategoryCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return ProductCategoryRules.CompanyRequired();
        }

        var companyId = currentUserService.CompanyId;
        var live = await ProductCategoryRules.LoadLiveAsync(unitOfWork, companyId);
        if (ProductCategoryRules.Validate(live, request, existingId: null) is { } error)
        {
            return error;
        }

        var entity = new ProductCategory
        {
            CompanyId = companyId,
            Name = request.Name.Trim(),
            ParentId = request.ParentId,
            SortOrder = request.SortOrder,
            IsActive = request.IsActive
        };

        await unitOfWork.GetRepository<ProductCategory>().InsertAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<ProductCategoryDto>
        {
            IsSuccess = true,
            Message = "Product category created successfully.",
            Data = ProductCategoryRules.ToDto(entity, live)
        };
    }
}

/// <summary>
/// Handles <see cref="UpdateProductCategoryCommand"/>.
/// </summary>
public sealed class UpdateProductCategoryCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<UpdateProductCategoryCommand, Response<ProductCategoryDto>>
{
    public async Task<Response<ProductCategoryDto>> Handle(UpdateProductCategoryCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return ProductCategoryRules.CompanyRequired();
        }

        var companyId = currentUserService.CompanyId;
        var repository = unitOfWork.GetRepository<ProductCategory>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != companyId || entity.GcRecord != 0)
        {
            return Response<ProductCategoryDto>.Error(CatalogErrorCodes.ProductCategoryNotFound, ["Product category not found."]);
        }

        var live = await ProductCategoryRules.LoadLiveAsync(unitOfWork, companyId);
        if (ProductCategoryRules.Validate(live, request, entity.Id) is { } error)
        {
            return error;
        }

        entity.Name = request.Name.Trim();
        entity.ParentId = request.ParentId;
        entity.SortOrder = request.SortOrder;
        entity.IsActive = request.IsActive;

        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<ProductCategoryDto>
        {
            IsSuccess = true,
            Message = "Product category updated successfully.",
            Data = ProductCategoryRules.ToDto(entity, live)
        };
    }
}

/// <summary>
/// Handles <see cref="DeleteProductCategoryCommand"/>.
/// </summary>
public sealed class DeleteProductCategoryCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteProductCategoryCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(DeleteProductCategoryCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error(CatalogErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var companyId = currentUserService.CompanyId;
        var repository = unitOfWork.GetRepository<ProductCategory>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != companyId || entity.GcRecord != 0)
        {
            return Response<Guid>.Error(CatalogErrorCodes.ProductCategoryNotFound, ["Product category not found."]);
        }

        var products = (await unitOfWork.GetRepository<Product>()
                .GetAllIncludingDeletedAsync(p => p.CompanyId == companyId && p.GcRecord == 0 && p.ProductCategoryId == entity.Id))
            .Count();
        var children = (await repository
                .GetAllIncludingDeletedAsync(c => c.CompanyId == companyId && c.GcRecord == 0 && c.ParentId == entity.Id))
            .Count();
        if (products > 0 || children > 0)
        {
            var detail = new List<string>();
            if (products > 0)
            {
                detail.Add($"Active Products: {products}");
            }

            if (children > 0)
            {
                detail.Add($"Active ProductCategories: {children}");
            }

            return Response<Guid>.Error(CatalogErrorCodes.ProductCategoryInUse, detail);
        }

        entity.MarkAsDeleted();
        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<Guid> { IsSuccess = true, Message = "Product category deleted successfully.", Data = entity.Id };
    }
}

/// <summary>
/// Handles <see cref="RestoreProductCategoryCommand"/> with <see cref="SoftDeleteRestorer"/> (SPEC 41-post).
/// </summary>
public sealed class RestoreProductCategoryCommandHandler(
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer)
    : IRequestHandler<RestoreProductCategoryCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(RestoreProductCategoryCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error(CatalogErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error(CatalogErrorCodes.SuperAdminRequired, ["Only a SuperAdmin can restore deleted records."]);
        }

        return await restorer.RestoreAsync<ProductCategory>(
            request.Id,
            request.CompanyId,
            isParentDeleted: async (entity, _) => entity.ParentId is { } parentId
                && !await restorer.AnyAsync<ProductCategory>(c => c.Id == parentId && c.GcRecord == 0),
            activeDuplicateExists: (entity, _) => restorer.AnyAsync<ProductCategory>(
                x => x.GcRecord == 0 && x.Id != entity.Id && x.CompanyId == entity.CompanyId && x.ParentId == entity.ParentId && x.Name == entity.Name),
            cancellationToken: cancellationToken);
    }
}

/// <summary>
/// Rules shared by the product category commands.
/// </summary>
internal static class ProductCategoryRules
{
    internal static Response<ProductCategoryDto> CompanyRequired() =>
        Response<ProductCategoryDto>.Error(CatalogErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);

    internal static async Task<List<ProductCategory>> LoadLiveAsync(IUnitOfWork unitOfWork, Guid companyId) =>
        (await unitOfWork.GetRepository<ProductCategory>()
            .GetAllIncludingDeletedAsync(c => c.CompanyId == companyId && c.GcRecord == 0))
        .ToList();

    /// <summary>
    /// Parent must be a live category of the company and not the category itself or one of its descendants
    /// (walking up from the parent must never reach <paramref name="existingId"/>); the name must be free among the
    /// live siblings of the same parent.
    /// </summary>
    internal static Response<ProductCategoryDto>? Validate(IReadOnlyCollection<ProductCategory> live, IProductCategoryInput input, Guid? existingId)
    {
        var byId = live.ToDictionary(c => c.Id);
        if (input.ParentId is { } parentId)
        {
            if (!byId.ContainsKey(parentId))
            {
                return Response<ProductCategoryDto>.Error(CatalogErrorCodes.ProductCategoryNotFound, ["Parent category not found."]);
            }

            var visited = new HashSet<Guid>();
            Guid? cursor = parentId;
            while (cursor is { } current && visited.Add(current))
            {
                if (current == existingId)
                {
                    return Response<ProductCategoryDto>.Error(CatalogErrorCodes.ProductCategoryCycle, ["A category cannot be its own ancestor."]);
                }

                cursor = byId.TryGetValue(current, out var node) ? node.ParentId : null;
            }
        }

        var name = input.Name.Trim();
        var duplicate = live.Any(c => c.Id != existingId && c.ParentId == input.ParentId && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        return duplicate
            ? Response<ProductCategoryDto>.Error(CatalogErrorCodes.DuplicateKey, ["Another category with the same parent already uses that name."])
            : null;
    }

    internal static ProductCategoryDto ToDto(ProductCategory entity, IReadOnlyCollection<ProductCategory> live) => new()
    {
        Id = entity.Id,
        GcRecord = entity.GcRecord,
        CompanyId = entity.CompanyId,
        Name = entity.Name,
        ParentId = entity.ParentId,
        ParentName = entity.ParentId is { } parentId ? live.FirstOrDefault(c => c.Id == parentId)?.Name : null,
        SortOrder = entity.SortOrder,
        IsActive = entity.IsActive,
        ChildrenCount = live.Count(c => c.ParentId == entity.Id),
        CreatedAt = entity.Created
    };
}
