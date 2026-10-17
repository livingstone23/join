using JOIN.Application.Common;
using JOIN.Application.DTO.Finance;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Finance;
using JOIN.Domain.Finance;
using MediatR;

namespace JOIN.Application.UseCases.Finance.Currencies.Commands;

/// <summary>
/// Handles <see cref="CreateCurrencyCommand"/>.
/// </summary>
public sealed class CreateCurrencyCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<CreateCurrencyCommand, Response<CurrencyDto>>
{
    public async Task<Response<CurrencyDto>> Handle(CreateCurrencyCommand request, CancellationToken cancellationToken)
    {
        var code = Currency.NormalizeCode(request.Code);
        if (await CurrencyRules.CodeExistsAsync(unitOfWork, code, exceptId: null))
        {
            return CurrencyRules.Duplicate();
        }

        var entity = new Currency
        {
            Code = code,
            Name = request.Name.Trim(),
            Symbol = request.Symbol.Trim(),
            Decimals = request.Decimals,
            IsActive = request.IsActive
        };

        await unitOfWork.GetRepository<Currency>().InsertAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<CurrencyDto>
        {
            IsSuccess = true,
            Message = "Currency created successfully.",
            Data = CurrencyRules.ToDto(entity, companiesCount: 0)
        };
    }
}

/// <summary>
/// Handles <see cref="UpdateCurrencyCommand"/>.
/// </summary>
public sealed class UpdateCurrencyCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateCurrencyCommand, Response<CurrencyDto>>
{
    public async Task<Response<CurrencyDto>> Handle(UpdateCurrencyCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.GetRepository<Currency>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.GcRecord != 0)
        {
            return Response<CurrencyDto>.Error(FinanceErrorCodes.CurrencyNotFound, ["Currency not found."]);
        }

        var code = Currency.NormalizeCode(request.Code);
        if (await CurrencyRules.CodeExistsAsync(unitOfWork, code, entity.Id))
        {
            return CurrencyRules.Duplicate();
        }

        entity.Code = code;
        entity.Name = request.Name.Trim();
        entity.Symbol = request.Symbol.Trim();
        entity.Decimals = request.Decimals;
        entity.IsActive = request.IsActive;

        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<CurrencyDto>
        {
            IsSuccess = true,
            Message = "Currency updated successfully.",
            Data = CurrencyRules.ToDto(entity, await CurrencyRules.CountCompaniesAsync(unitOfWork, entity.Id))
        };
    }
}

/// <summary>
/// Handles <see cref="DeleteCurrencyCommand"/>.
/// </summary>
public sealed class DeleteCurrencyCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteCurrencyCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(DeleteCurrencyCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.GetRepository<Currency>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.GcRecord != 0)
        {
            return Response<Guid>.Error(FinanceErrorCodes.CurrencyNotFound, ["Currency not found."]);
        }

        var companies = await CurrencyRules.CountCompaniesAsync(unitOfWork, entity.Id);
        if (companies > 0)
        {
            return Response<Guid>.Error(FinanceErrorCodes.CurrencyInUse, [$"Active CompanyCurrencies: {companies}"]);
        }

        entity.MarkAsDeleted();
        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<Guid> { IsSuccess = true, Message = "Currency deleted successfully.", Data = entity.Id };
    }
}

/// <summary>
/// Handles <see cref="RestoreCurrencyCommand"/> with <see cref="SoftDeleteRestorer"/> (SPEC 41-post).
/// </summary>
public sealed class RestoreCurrencyCommandHandler(SoftDeleteRestorer restorer)
    : IRequestHandler<RestoreCurrencyCommand, Response<Guid>>
{
    public Task<Response<Guid>> Handle(RestoreCurrencyCommand request, CancellationToken cancellationToken) =>
        restorer.RestoreAsync<Currency>(
            request.Id,
            requestedCompanyId: null,
            activeDuplicateExists: (entity, _) => restorer.AnyAsync<Currency>(
                x => x.GcRecord == 0 && x.Id != entity.Id && x.Code == entity.Code),
            cancellationToken: cancellationToken);
}

/// <summary>
/// Rules shared by the currency commands.
/// </summary>
internal static class CurrencyRules
{
    internal static Response<CurrencyDto> Duplicate() =>
        Response<CurrencyDto>.Error(FinanceErrorCodes.DuplicateKey, ["Another currency already uses the same code."]);

    internal static async Task<bool> CodeExistsAsync(IUnitOfWork unitOfWork, string code, Guid? exceptId) =>
        (await unitOfWork.GetRepository<Currency>()
            .GetAllIncludingDeletedAsync(c => c.GcRecord == 0 && c.Code == code && c.Id != exceptId))
        .Any();

    /// <summary>Live company rows (any company) that use the currency.</summary>
    internal static async Task<int> CountCompaniesAsync(IUnitOfWork unitOfWork, Guid currencyId) =>
        (await unitOfWork.GetRepository<CompanyCurrency>()
            .GetAllIncludingDeletedAsync(c => c.CurrencyId == currencyId && c.GcRecord == 0))
        .Count();

    internal static CurrencyDto ToDto(Currency entity, int companiesCount) => new()
    {
        Id = entity.Id,
        GcRecord = entity.GcRecord,
        Code = entity.Code,
        Name = entity.Name,
        Symbol = entity.Symbol,
        Decimals = entity.Decimals,
        IsActive = entity.IsActive,
        CompaniesCount = companiesCount,
        CreatedAt = entity.Created
    };
}
