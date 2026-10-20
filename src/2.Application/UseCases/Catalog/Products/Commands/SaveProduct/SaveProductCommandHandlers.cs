using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Catalog;
using JOIN.Application.UseCases.Catalog.ProductAttachments;
using JOIN.Application.UseCases.Finance.CompanyCurrencies;
using JOIN.Domain.Catalog;
using JOIN.Domain.Finance;
using MediatR;

namespace JOIN.Application.UseCases.Catalog.Products.Commands;

/// <summary>Handles <see cref="DeleteProductCommand"/> with cascade to images and documents (SPEC 70).</summary>
public sealed class DeleteProductCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    ProductAttachmentCascadeCoordinator cascade)
    : IRequestHandler<DeleteProductCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(DeleteProductCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error(CatalogErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var repository = unitOfWork.GetRepository<Product>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != currentUserService.CompanyId || entity.GcRecord != 0)
        {
            return Response<Guid>.Error(CatalogErrorCodes.ProductNotFound, ["Product not found."]);
        }

        // SPEC 61 B.14: a product offered in a live lead line is deactivated, not deleted.
        var check = await new ActiveDependentsCheck(unitOfWork).CountAsync<JOIN.Domain.Customers.Leads.LeadProduct>(
            l => l.GcRecord == 0 && l.ProductId == entity.Id, "LeadProducts");
        if (check.HasDependents)
        {
            return Response<Guid>.Error(CatalogErrorCodes.ProductInUse, [.. check.Details]);
        }

        // SPEC 70 — cascade the soft-delete to images and documents. The same GcRecord stamp is used
        // so the restore can find them.
        entity.MarkAsDeleted();
        var stamp = entity.GcRecord;
        await repository.UpdateAsync(entity);
        await cascade.MarkChildrenAsDeletedAsync(entity.Id, stamp, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<Guid> { IsSuccess = true, Message = "Product deleted successfully.", Data = entity.Id };
    }
}

/// <summary>Handles <see cref="RestoreProductCommand"/> with <see cref="SoftDeleteRestorer"/> (SPEC 41-post) and cascade restore (SPEC 70).</summary>
public sealed class RestoreProductCommandHandler(
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer,
    ProductAttachmentCascadeCoordinator cascade)
    : IRequestHandler<RestoreProductCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(RestoreProductCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error(CatalogErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error(CatalogErrorCodes.SuperAdminRequired, ["Only a SuperAdmin can restore deleted records."]);
        }

        return await restorer.RestoreAsync<Product>(
            request.Id,
            request.CompanyId,
            activeDuplicateExists: (entity, _) => restorer.AnyAsync<Product>(
                x => x.GcRecord == 0 && x.Id != entity.Id && x.CompanyId == entity.CompanyId && x.Code == entity.Code),
            restoreCascade: (entity, stamp, ct) => cascade.RestoreChildrenAsync(entity.Id, stamp, ct),
            cancellationToken: cancellationToken);
    }
}
/// <summary>Handles <see cref="CreateProductCommand"/>.</summary>
public sealed class CreateProductCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    CompanyBaseCurrencyCoordinator baseCurrency)
    : IRequestHandler<CreateProductCommand, Response<ProductDto>>
{
    public async Task<Response<ProductDto>> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return ProductRules.CompanyRequired();
        }

        var companyId = currentUserService.CompanyId;
        var check = await ProductRules.ValidateAsync(unitOfWork, baseCurrency, companyId, request, exceptId: null);
        if (check.Error is not null)
        {
            return check.Error;
        }

        var entity = new Product { CompanyId = companyId };
        ProductRules.Apply(entity, request);

        await unitOfWork.GetRepository<Product>().InsertAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<ProductDto>
        {
            IsSuccess = true,
            Message = "Product created successfully.",
            Data = ProductRules.ToDto(entity, check.CategoryName, check.BaseCode)
        };
    }
}

/// <summary>Handles <see cref="UpdateProductCommand"/>.</summary>
public sealed class UpdateProductCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    CompanyBaseCurrencyCoordinator baseCurrency)
    : IRequestHandler<UpdateProductCommand, Response<ProductDto>>
{
    public async Task<Response<ProductDto>> Handle(UpdateProductCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return ProductRules.CompanyRequired();
        }

        var companyId = currentUserService.CompanyId;
        var repository = unitOfWork.GetRepository<Product>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != companyId || entity.GcRecord != 0)
        {
            return Response<ProductDto>.Error(CatalogErrorCodes.ProductNotFound, ["Product not found."]);
        }

        var check = await ProductRules.ValidateAsync(unitOfWork, baseCurrency, companyId, request, entity.Id);
        if (check.Error is not null)
        {
            return check.Error;
        }

        ProductRules.Apply(entity, request);
        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<ProductDto>
        {
            IsSuccess = true,
            Message = "Product updated successfully.",
            Data = ProductRules.ToDto(entity, check.CategoryName, check.BaseCode)
        };
    }
}

/// <summary>
/// Rules shared by the product commands (validation, apply, DTO mapping). Lives here so every handler in the
/// folder sees the same defaults.
/// </summary>
internal static class ProductRules
{
    internal sealed record Check(Response<ProductDto>? Error, string? CategoryName, string BaseCode);

    internal static Response<ProductDto> CompanyRequired() =>
        Response<ProductDto>.Error(CatalogErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);

    internal static async Task<Check> ValidateAsync(
        IUnitOfWork unitOfWork,
        CompanyBaseCurrencyCoordinator baseCurrency,
        Guid companyId,
        IProductInput input,
        Guid? exceptId)
    {
        var baseRow = await baseCurrency.GetBaseAsync(companyId);
        if (baseRow is null)
        {
            return new Check(Response<ProductDto>.Error(CatalogErrorCodes.BaseCurrencyRequired, ["The company has no base currency."]), null, string.Empty);
        }

        var baseCode = (await unitOfWork.GetRepository<Currency>().GetIncludingDeletedAsync(baseRow.CurrencyId))?.Code ?? string.Empty;

        string? categoryName = null;
        if (input.ProductCategoryId is { } categoryId)
        {
            var category = await unitOfWork.GetRepository<ProductCategory>().GetAsync(categoryId);
            if (category is null || category.CompanyId != companyId || category.GcRecord != 0)
            {
                return new Check(Response<ProductDto>.Error(CatalogErrorCodes.ProductCategoryNotFound, ["Product category not found."]), null, baseCode);
            }

            categoryName = category.Name;
        }

        var code = Product.NormalizeCode(input.Code);
        var duplicate = (await unitOfWork.GetRepository<Product>()
                .GetAllIncludingDeletedAsync(p => p.CompanyId == companyId && p.GcRecord == 0 && p.Code == code && p.Id != exceptId))
            .Any();

        return duplicate
            ? new Check(Response<ProductDto>.Error(CatalogErrorCodes.DuplicateKey, ["Another product of the company already uses the same code."]), categoryName, baseCode)
            : new Check(null, categoryName, baseCode);
    }

    internal static void Apply(Product entity, IProductInput input)
    {
        entity.Code = Product.NormalizeCode(input.Code);
        entity.Name = input.Name.Trim();
        entity.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        entity.Kind = (ProductKind)input.Kind;
        entity.ProductCategoryId = input.ProductCategoryId;
        entity.ReferencePrice = input.ReferencePrice;
        entity.IsActive = input.IsActive;
    }

    internal static ProductDto ToDto(Product entity, string? categoryName, string baseCode) => new()
    {
        Id = entity.Id,
        GcRecord = entity.GcRecord,
        CompanyId = entity.CompanyId,
        Code = entity.Code,
        Name = entity.Name,
        Description = entity.Description,
        Kind = (int)entity.Kind,
        ProductCategoryId = entity.ProductCategoryId,
        CategoryName = categoryName,
        ReferencePrice = entity.ReferencePrice,
        BaseCurrencyCode = baseCode,
        IsActive = entity.IsActive,
        CreatedAt = entity.Created
    };
}
