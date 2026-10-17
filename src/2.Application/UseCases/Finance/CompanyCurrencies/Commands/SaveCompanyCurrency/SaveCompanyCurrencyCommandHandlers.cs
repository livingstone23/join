using JOIN.Application.Common;
using JOIN.Application.DTO.Finance;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Finance;
using JOIN.Domain.Finance;
using MediatR;

namespace JOIN.Application.UseCases.Finance.CompanyCurrencies.Commands;

/// <summary>
/// Handles <see cref="CreateCompanyCurrencyCommand"/>.
/// </summary>
public sealed class CreateCompanyCurrencyCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    CompanyBaseCurrencyCoordinator baseCoordinator,
    CompanyBaseCurrencyChangeGuard changeGuard)
    : IRequestHandler<CreateCompanyCurrencyCommand, Response<CompanyCurrencyDto>>
{
    public async Task<Response<CompanyCurrencyDto>> Handle(CreateCompanyCurrencyCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return CompanyCurrencyRules.CompanyRequired();
        }

        var companyId = currentUserService.CompanyId;
        var currency = await unitOfWork.GetRepository<Currency>().GetAsync(request.CurrencyId);
        if (currency is null || currency.GcRecord != 0 || !currency.IsActive)
        {
            return Response<CompanyCurrencyDto>.Error(FinanceErrorCodes.InvalidCurrency, ["The currency does not exist or is inactive."]);
        }

        var duplicate = (await unitOfWork.GetRepository<CompanyCurrency>()
                .GetAllIncludingDeletedAsync(c => c.CompanyId == companyId && c.CurrencyId == request.CurrencyId && c.GcRecord == 0))
            .Any();
        if (duplicate)
        {
            return Response<CompanyCurrencyDto>.Error(FinanceErrorCodes.DuplicateKey, ["The company already uses this currency."]);
        }

        if (request.IsBase)
        {
            if (!request.IsActive)
            {
                return CompanyCurrencyRules.BaseRequired("The base currency must be active.");
            }

            if (await CompanyCurrencyRules.BaseChangeBlockedAsync(baseCoordinator, changeGuard, companyId, cancellationToken) is { } blocked)
            {
                return blocked;
            }

            await baseCoordinator.ClearOtherBasesAsync(companyId, Guid.Empty, cancellationToken);
        }

        var entity = new CompanyCurrency
        {
            CompanyId = companyId,
            CurrencyId = currency.Id,
            IsBase = request.IsBase,
            IsActive = request.IsActive
        };

        await unitOfWork.GetRepository<CompanyCurrency>().InsertAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<CompanyCurrencyDto>
        {
            IsSuccess = true,
            Message = "Company currency created successfully.",
            Data = CompanyCurrencyRules.ToDto(entity, currency)
        };
    }
}

/// <summary>
/// Handles <see cref="UpdateCompanyCurrencyCommand"/>.
/// </summary>
public sealed class UpdateCompanyCurrencyCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    CompanyBaseCurrencyCoordinator baseCoordinator,
    CompanyBaseCurrencyChangeGuard changeGuard)
    : IRequestHandler<UpdateCompanyCurrencyCommand, Response<CompanyCurrencyDto>>
{
    public async Task<Response<CompanyCurrencyDto>> Handle(UpdateCompanyCurrencyCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return CompanyCurrencyRules.CompanyRequired();
        }

        var companyId = currentUserService.CompanyId;
        var repository = unitOfWork.GetRepository<CompanyCurrency>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != companyId || entity.GcRecord != 0)
        {
            return Response<CompanyCurrencyDto>.Error(FinanceErrorCodes.CompanyCurrencyNotFound, ["Company currency not found."]);
        }

        if (entity.IsBase && !request.IsBase)
        {
            return CompanyCurrencyRules.BaseRequired("The base currency cannot be unmarked. Mark another currency as base instead.");
        }

        if ((entity.IsBase || request.IsBase) && !request.IsActive)
        {
            return CompanyCurrencyRules.BaseRequired("The base currency must be active.");
        }

        var currency = await unitOfWork.GetRepository<Currency>().GetIncludingDeletedAsync(entity.CurrencyId);
        if (!entity.IsBase && request.IsBase)
        {
            if (currency is null || currency.GcRecord != 0 || !currency.IsActive)
            {
                return Response<CompanyCurrencyDto>.Error(FinanceErrorCodes.InvalidCurrency, ["The currency does not exist or is inactive."]);
            }

            if (await CompanyCurrencyRules.BaseChangeBlockedAsync(baseCoordinator, changeGuard, companyId, cancellationToken) is { } blocked)
            {
                return blocked;
            }

            await baseCoordinator.ClearOtherBasesAsync(companyId, entity.Id, cancellationToken);
            entity.IsBase = true;
        }

        entity.IsActive = request.IsActive;
        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<CompanyCurrencyDto>
        {
            IsSuccess = true,
            Message = "Company currency updated successfully.",
            Data = CompanyCurrencyRules.ToDto(entity, currency)
        };
    }
}

/// <summary>
/// Handles <see cref="DeleteCompanyCurrencyCommand"/>.
/// </summary>
public sealed class DeleteCompanyCurrencyCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteCompanyCurrencyCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(DeleteCompanyCurrencyCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error(FinanceErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var repository = unitOfWork.GetRepository<CompanyCurrency>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != currentUserService.CompanyId || entity.GcRecord != 0)
        {
            return Response<Guid>.Error(FinanceErrorCodes.CompanyCurrencyNotFound, ["Company currency not found."]);
        }

        if (entity.IsBase)
        {
            return Response<Guid>.Error(FinanceErrorCodes.BaseCurrencyRequired, ["The base currency cannot be deleted. Mark another currency as base first."]);
        }

        entity.MarkAsDeleted();
        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<Guid> { IsSuccess = true, Message = "Company currency deleted successfully.", Data = entity.Id };
    }
}

/// <summary>
/// Handles <see cref="RestoreCompanyCurrencyCommand"/> with <see cref="SoftDeleteRestorer"/> (SPEC 41-post).
/// </summary>
public sealed class RestoreCompanyCurrencyCommandHandler(
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer)
    : IRequestHandler<RestoreCompanyCurrencyCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(RestoreCompanyCurrencyCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error(FinanceErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error(FinanceErrorCodes.SuperAdminRequired, ["Only a SuperAdmin can restore deleted records."]);
        }

        return await restorer.RestoreAsync<CompanyCurrency>(
            request.Id,
            request.CompanyId,
            activeDuplicateExists: (entity, _) => restorer.AnyAsync<CompanyCurrency>(
                x => x.GcRecord == 0 && x.Id != entity.Id && x.CompanyId == entity.CompanyId && x.CurrencyId == entity.CurrencyId),
            beforeRestore: async (entity, _) =>
            {
                if (entity.IsBase && await restorer.AnyAsync<CompanyCurrency>(
                        x => x.GcRecord == 0 && x.Id != entity.Id && x.CompanyId == entity.CompanyId && x.IsBase))
                {
                    entity.IsBase = false;
                }
            },
            cancellationToken: cancellationToken);
    }
}

/// <summary>
/// Rules shared by the company currency commands.
/// </summary>
internal static class CompanyCurrencyRules
{
    internal static Response<CompanyCurrencyDto> CompanyRequired() =>
        Response<CompanyCurrencyDto>.Error(FinanceErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);

    internal static Response<CompanyCurrencyDto> BaseRequired(string detail) =>
        Response<CompanyCurrencyDto>.Error(FinanceErrorCodes.BaseCurrencyRequired, [detail]);

    /// <summary>
    /// SPEC 56 rule 6: when the company already has a base and stores amounts, changing the base is blocked.
    /// A company without a base (not expected after the seed) can always set one.
    /// </summary>
    internal static async Task<Response<CompanyCurrencyDto>?> BaseChangeBlockedAsync(
        CompanyBaseCurrencyCoordinator baseCoordinator,
        CompanyBaseCurrencyChangeGuard changeGuard,
        Guid companyId,
        CancellationToken cancellationToken)
    {
        if (await baseCoordinator.GetBaseAsync(companyId) is null)
        {
            return null;
        }

        var sources = await changeGuard.GetSourcesWithAmountsAsync(companyId, cancellationToken);
        return sources.Count == 0
            ? null
            : Response<CompanyCurrencyDto>.Error(
                FinanceErrorCodes.BaseCurrencyInUse,
                sources.Select(s => $"The company already stores amounts in {s}.").ToList());
    }

    internal static CompanyCurrencyDto ToDto(CompanyCurrency entity, Currency? currency) => new()
    {
        Id = entity.Id,
        GcRecord = entity.GcRecord,
        CompanyId = entity.CompanyId,
        CurrencyId = entity.CurrencyId,
        Code = currency?.Code ?? string.Empty,
        Name = currency?.Name ?? string.Empty,
        Symbol = currency?.Symbol ?? string.Empty,
        Decimals = currency?.Decimals ?? 2,
        IsBase = entity.IsBase,
        IsActive = entity.IsActive,
        CreatedAt = entity.Created
    };
}
