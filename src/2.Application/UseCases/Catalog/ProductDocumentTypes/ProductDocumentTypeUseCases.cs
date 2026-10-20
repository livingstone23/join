using Dapper;
using FluentValidation;
using JOIN.Application.Common;
using JOIN.Application.DTO.Catalog;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Catalog;
using MediatR;
using Settings = JOIN.Domain.Catalog.ProductDocumentType;

namespace JOIN.Application.UseCases.Catalog.ProductDocumentTypes;

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

/// <summary>SPEC 70 — <c>DELETE /ProductDocumentTypes/{id}</c>: blocked by <c>PRODUCT_DOCUMENT_TYPE_IN_USE</c> when there are live documents.</summary>
public sealed record DeleteProductDocumentTypeCommand(Guid Id) : ITransactionalCommand<Response<Guid>>;

/// <summary>SPEC 70 — <c>POST /ProductDocumentTypes/{id}/restore</c>: SuperAdmin only.</summary>
public sealed record RestoreProductDocumentTypeCommand(Guid Id, Guid? CompanyId = null) : ITransactionalCommand<Response<Guid>>;

/// <summary><c>GET /ProductDocumentTypes</c>: the company's live catalog (optional inactive rows).</summary>
public sealed record GetProductDocumentTypesQuery(bool? IncludeInactive = null) : IRequest<Response<IReadOnlyList<ProductDocumentTypeDto>>>;

internal static class ProductDocumentTypeRules
{
    internal static void Apply(Settings entity, IProductDocumentTypeInput input)
    {
        entity.Code = Settings.NormalizeCode(input.Code);
        entity.Name = input.Name.Trim();
        entity.SortOrder = input.SortOrder;
        entity.IsActive = input.IsActive;
    }

    internal static ProductDocumentTypeDto ToDto(Settings s) => new()
    {
        Id = s.Id,
        CompanyId = s.CompanyId,
        Code = s.Code,
        Name = s.Name,
        SortOrder = s.SortOrder,
        IsActive = s.IsActive,
        CreatedAt = s.Created
    };
}

internal sealed class CreateProductDocumentTypeCommandValidator : AbstractValidator<CreateProductDocumentTypeCommand>
{
    public CreateProductDocumentTypeCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(Settings.MaxCodeLength);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Settings.MaxNameLength);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
    }
}

internal sealed class UpdateProductDocumentTypeCommandValidator : AbstractValidator<UpdateProductDocumentTypeCommand>
{
    public UpdateProductDocumentTypeCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(Settings.MaxCodeLength);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Settings.MaxNameLength);
        RuleFor(x => x.SortOrder).GreaterThanOrEqualTo(0);
    }
}

internal static class ProductDocumentTypeCatalogQuery
{
    internal static Response<T> CompanyRequired<T>() =>
        Response<T>.Error(CatalogErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
}

/// <summary>Handles <see cref="CreateProductDocumentTypeCommand"/>.</summary>
public sealed class CreateProductDocumentTypeCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<CreateProductDocumentTypeCommand, Response<ProductDocumentTypeDto>>
{
    public async Task<Response<ProductDocumentTypeDto>> Handle(CreateProductDocumentTypeCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return ProductDocumentTypeCatalogQuery.CompanyRequired<ProductDocumentTypeDto>();
        }

        var companyId = currentUserService.CompanyId;
        var repository = unitOfWork.GetRepository<Settings>();

        var normalizedCode = Settings.NormalizeCode(request.Code);
        var duplicate = (await repository.GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.Code == normalizedCode && x.GcRecord == 0)).Any();
        if (duplicate)
        {
            return Response<ProductDocumentTypeDto>.Error(
                CatalogErrorCodes.ProductDocumentTypeAlreadyExists,
                ["An active product document type with that code already exists."]);
        }

        var entity = new Settings { CompanyId = companyId };
        ProductDocumentTypeRules.Apply(entity, request);
        await repository.InsertAsync(entity);
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
            return ProductDocumentTypeCatalogQuery.CompanyRequired<ProductDocumentTypeDto>();
        }

        var companyId = currentUserService.CompanyId;
        var repository = unitOfWork.GetRepository<Settings>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != companyId || entity.GcRecord != 0)
        {
            return Response<ProductDocumentTypeDto>.Error(
                CatalogErrorCodes.ProductDocumentTypeNotFound,
                ["Product document type not found."]);
        }

        var normalizedCode = Settings.NormalizeCode(request.Code);
        var duplicate = (await repository.GetAllIncludingDeletedAsync(
            x => x.CompanyId == companyId
                && x.Id != request.Id
                && x.Code == normalizedCode
                && x.GcRecord == 0)).Any();
        if (duplicate)
        {
            return Response<ProductDocumentTypeDto>.Error(
                CatalogErrorCodes.ProductDocumentTypeAlreadyExists,
                ["An active product document type with that code already exists."]);
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

/// <summary>Handles <see cref="DeleteProductDocumentTypeCommand"/> — blocks when live documents reference the type.</summary>
public sealed class DeleteProductDocumentTypeCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteProductDocumentTypeCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(DeleteProductDocumentTypeCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return ProductDocumentTypeCatalogQuery.CompanyRequired<Guid>();
        }

        var companyId = currentUserService.CompanyId;
        var repository = unitOfWork.GetRepository<Settings>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != companyId || entity.GcRecord != 0)
        {
            return Response<Guid>.Error(
                CatalogErrorCodes.ProductDocumentTypeNotFound,
                ["Product document type not found."]);
        }

        var check = await new ActiveDependentsCheck(unitOfWork).CountAsync<JOIN.Domain.Catalog.ProductDocument>(
            x => x.GcRecord == 0 && x.ProductDocumentTypeId == entity.Id,
            "Active ProductDocuments");
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
            return ProductDocumentTypeCatalogQuery.CompanyRequired<Guid>();
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error(CatalogErrorCodes.SuperAdminRequired, ["Only a SuperAdmin can restore deleted records."]);
        }

        return await restorer.RestoreAsync<Settings>(
            request.Id,
            request.CompanyId,
            activeDuplicateExists: (entity, _) => restorer.AnyAsync<Settings>(
                x => x.GcRecord == 0 && x.Id != entity.Id && x.CompanyId == entity.CompanyId && x.Code == entity.Code),
            cancellationToken: cancellationToken);
    }
}

/// <summary>Handles <see cref="GetProductDocumentTypesQuery"/> with Dapper.</summary>
public sealed class GetProductDocumentTypesQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetProductDocumentTypesQuery, Response<IReadOnlyList<ProductDocumentTypeDto>>>
{
    public async Task<Response<IReadOnlyList<ProductDocumentTypeDto>>> Handle(GetProductDocumentTypesQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return ProductDocumentTypeCatalogQuery.CompanyRequired<IReadOnlyList<ProductDocumentTypeDto>>();
        }

        const string sql = """
            SELECT Id, CompanyId, Code, Name, SortOrder, IsActive, Created AS CreatedAt
            FROM catalog.productdocumenttypes
            WHERE CompanyId = @CompanyId
              AND GcRecord = 0
              AND (@IncludeInactive = TRUE OR IsActive = TRUE)
            ORDER BY SortOrder, Name;
            """;

        using var connection = connectionFactory.CreateConnection();
        var rows = await connection.QueryAsync<ProductDocumentTypeDto>(new CommandDefinition(
            sql,
            new { CompanyId = currentUserService.CompanyId, IncludeInactive = request.IncludeInactive == true },
            cancellationToken: cancellationToken));

        return new Response<IReadOnlyList<ProductDocumentTypeDto>>
        {
            IsSuccess = true,
            Message = "Product document types retrieved successfully.",
            Data = rows.AsList()
        };
    }
}