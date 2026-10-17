using System.Linq.Expressions;
using FluentValidation;
using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.LeadSettings;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Customers;
using JOIN.Domain.Audit;
using JOIN.Domain.Customers.Leads;
using MediatR;

namespace JOIN.Application.UseCases.Customers.LeadSettings.Common;

/// <summary>Fields shared by the create and update commands of a commercial catalog row (SPEC 59).</summary>
public interface ICommercialCatalogInput
{
    string Code { get; }
    string Name { get; }
    int SortOrder { get; }
    bool IsActive { get; }
}

/// <summary>Update commands carry the id of the row.</summary>
public interface ICommercialCatalogUpdate : ICommercialCatalogInput
{
    Guid Id { get; }
}

/// <summary>Validation shared by the commercial catalog commands.</summary>
public static class CommercialCatalogInputRules
{
    public static void Apply<T>(AbstractValidator<T> validator) where T : ICommercialCatalogInput
    {
        validator.RuleFor(x => x.Code)
            .Must(code => !string.IsNullOrWhiteSpace(code)).WithMessage("Code is required.")
            .Must(code => CommercialCode.Normalize(code ?? string.Empty).Length <= 30).WithMessage("Code cannot exceed 30 characters.");
        validator.RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}

/// <summary>
/// Data access and rules shared by every commercial catalog (SPEC 59): live rows of the company, unique code and name.
/// </summary>
public abstract class CommercialCatalogRules<TEntity, TDto>
    where TEntity : BaseTenantEntity, ICommercialCatalogItem
    where TDto : CommercialCatalogItemDto
{
    /// <summary><c>*_NOT_FOUND</c> code of the catalog.</summary>
    public abstract string NotFoundCode { get; }

    /// <summary>Human label used in messages ("lead source").</summary>
    public abstract string Label { get; }

    public abstract TDto ToDto(TEntity entity);

    public static async Task<List<TEntity>> LoadLiveAsync(IUnitOfWork unitOfWork, Guid companyId) =>
        (await unitOfWork.GetRepository<TEntity>().GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.GcRecord == 0)).ToList();

    public Response<TDto>? CheckDuplicates(ICommercialCatalogInput input, IEnumerable<TEntity> live, Guid? exceptId)
    {
        var code = CommercialCode.Normalize(input.Code);
        var name = input.Name.Trim();
        var duplicate = live.Any(x => x.Id != exceptId
            && (string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)));
        return duplicate
            ? Response<TDto>.Error(LeadSettingsErrorCodes.DuplicateKey, [$"Another {Label} of the company already uses the same code or name."])
            : null;
    }

    public static void ApplyBase(TEntity entity, ICommercialCatalogInput input)
    {
        entity.Code = CommercialCode.Normalize(input.Code);
        entity.Name = input.Name.Trim();
        entity.SortOrder = input.SortOrder;
        entity.IsActive = input.IsActive;
    }

    public static Response<T> CompanyRequired<T>() =>
        Response<T>.Error(LeadSettingsErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
}

/// <summary>Create handler shared by the commercial catalogs.</summary>
public abstract class CommercialCatalogCreateHandler<TCommand, TEntity, TDto>(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    CommercialCatalogRules<TEntity, TDto> rules) : IRequestHandler<TCommand, Response<TDto>>
    where TCommand : ICommercialCatalogInput, IRequest<Response<TDto>>
    where TEntity : BaseTenantEntity, ICommercialCatalogItem, new()
    where TDto : CommercialCatalogItemDto
{
    /// <summary>Copies the catalog-specific fields.</summary>
    protected abstract void ApplyExtra(TCommand command, TEntity entity);

    /// <summary>Catalog-specific rules; <c>null</c> when valid.</summary>
    protected virtual Response<TDto>? Validate(TCommand command, IReadOnlyList<TEntity> live) => null;

    public async Task<Response<TDto>> Handle(TCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return CommercialCatalogRules<TEntity, TDto>.CompanyRequired<TDto>();
        }

        var companyId = currentUserService.CompanyId;
        var live = await CommercialCatalogRules<TEntity, TDto>.LoadLiveAsync(unitOfWork, companyId);
        if ((rules.CheckDuplicates(request, live, null) ?? Validate(request, live)) is { } error)
        {
            return error;
        }

        var entity = new TEntity { CompanyId = companyId };
        CommercialCatalogRules<TEntity, TDto>.ApplyBase(entity, request);
        ApplyExtra(request, entity);

        await unitOfWork.GetRepository<TEntity>().InsertAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<TDto> { IsSuccess = true, Message = $"The {rules.Label} was created successfully.", Data = rules.ToDto(entity) };
    }
}

/// <summary>Update handler shared by the commercial catalogs.</summary>
public abstract class CommercialCatalogUpdateHandler<TCommand, TEntity, TDto>(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    CommercialCatalogRules<TEntity, TDto> rules) : IRequestHandler<TCommand, Response<TDto>>
    where TCommand : ICommercialCatalogUpdate, IRequest<Response<TDto>>
    where TEntity : BaseTenantEntity, ICommercialCatalogItem
    where TDto : CommercialCatalogItemDto
{
    protected abstract void ApplyExtra(TCommand command, TEntity entity);

    protected virtual Response<TDto>? Validate(TCommand command, TEntity existing, IReadOnlyList<TEntity> live) => null;

    public async Task<Response<TDto>> Handle(TCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return CommercialCatalogRules<TEntity, TDto>.CompanyRequired<TDto>();
        }

        var companyId = currentUserService.CompanyId;
        var repository = unitOfWork.GetRepository<TEntity>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != companyId || entity.GcRecord != 0)
        {
            return Response<TDto>.Error(rules.NotFoundCode, [$"The {rules.Label} was not found."]);
        }

        var live = await CommercialCatalogRules<TEntity, TDto>.LoadLiveAsync(unitOfWork, companyId);
        if ((rules.CheckDuplicates(request, live, entity.Id) ?? Validate(request, entity, live)) is { } error)
        {
            return error;
        }

        CommercialCatalogRules<TEntity, TDto>.ApplyBase(entity, request);
        ApplyExtra(request, entity);

        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<TDto> { IsSuccess = true, Message = $"The {rules.Label} was updated successfully.", Data = rules.ToDto(entity) };
    }
}

/// <summary>Delete (logical) handler shared by the commercial catalogs. SPEC 61 adds the in-use checks.</summary>
public abstract class CommercialCatalogDeleteHandler<TCommand, TEntity, TDto>(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    CommercialCatalogRules<TEntity, TDto> rules) : IRequestHandler<TCommand, Response<Guid>>
    where TCommand : IRequest<Response<Guid>>
    where TEntity : BaseTenantEntity, ICommercialCatalogItem
    where TDto : CommercialCatalogItemDto
{
    protected abstract Guid IdOf(TCommand command);

    /// <summary>Catalog-specific rules before deleting; <c>null</c> when allowed.</summary>
    protected virtual Task<Response<Guid>?> ValidateAsync(TEntity entity, CancellationToken cancellationToken) => Task.FromResult<Response<Guid>?>(null);

    public async Task<Response<Guid>> Handle(TCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return CommercialCatalogRules<TEntity, TDto>.CompanyRequired<Guid>();
        }

        var repository = unitOfWork.GetRepository<TEntity>();
        var entity = await repository.GetAsync(IdOf(request));
        if (entity is null || entity.CompanyId != currentUserService.CompanyId || entity.GcRecord != 0)
        {
            return Response<Guid>.Error(rules.NotFoundCode, [$"The {rules.Label} was not found."]);
        }

        if (await ValidateAsync(entity, cancellationToken) is { } error)
        {
            return error;
        }

        entity.MarkAsDeleted();
        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<Guid> { IsSuccess = true, Message = $"The {rules.Label} was deleted successfully.", Data = entity.Id };
    }
}

/// <summary>Restore handler shared by the commercial catalogs (SPEC 41-post, SuperAdmin).</summary>
public abstract class CommercialCatalogRestoreHandler<TCommand, TEntity>(
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer) : IRequestHandler<TCommand, Response<Guid>>
    where TCommand : IRequest<Response<Guid>>
    where TEntity : BaseTenantEntity, ICommercialCatalogItem
{
    protected abstract Guid IdOf(TCommand command);
    protected abstract Guid? CompanyOf(TCommand command);

    /// <summary>Live rows that would collide with the restored one in a unique index.</summary>
    protected virtual Expression<Func<TEntity, bool>> DuplicateOf(TEntity entity) =>
        x => x.GcRecord == 0 && x.Id != entity.Id && x.CompanyId == entity.CompanyId && (x.Code == entity.Code || x.Name == entity.Name);

    public async Task<Response<Guid>> Handle(TCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error(LeadSettingsErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error(LeadSettingsErrorCodes.SuperAdminRequired, ["Only a SuperAdmin can restore deleted records."]);
        }

        return await restorer.RestoreAsync<TEntity>(
            IdOf(request),
            CompanyOf(request),
            activeDuplicateExists: (entity, _) => restorer.AnyAsync(DuplicateOf(entity)),
            cancellationToken: cancellationToken);
    }
}
