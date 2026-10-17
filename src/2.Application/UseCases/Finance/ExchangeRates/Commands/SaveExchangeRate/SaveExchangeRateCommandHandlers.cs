using JOIN.Application.Common;
using JOIN.Application.DTO.Finance;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Services.Finance;
using JOIN.Domain.Finance;
using MediatR;

namespace JOIN.Application.UseCases.Finance.ExchangeRates.Commands;

/// <summary>
/// Handles <see cref="CreateExchangeRateCommand"/>.
/// </summary>
public sealed class CreateExchangeRateCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<CreateExchangeRateCommand, Response<ExchangeRateDto>>
{
    public async Task<Response<ExchangeRateDto>> Handle(CreateExchangeRateCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return ExchangeRateRules.CompanyRequired();
        }

        var companyId = currentUserService.CompanyId;
        var check = await ExchangeRateRules.ValidateAsync(unitOfWork, companyId, request, exceptId: null);
        if (check.Error is not null)
        {
            return check.Error;
        }

        var entity = new ExchangeRate
        {
            CompanyId = companyId,
            CurrencyId = request.CurrencyId,
            RateDate = request.RateDate,
            Rate = request.Rate
        };

        await unitOfWork.GetRepository<ExchangeRate>().InsertAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<ExchangeRateDto>
        {
            IsSuccess = true,
            Message = "Exchange rate created successfully.",
            Data = ExchangeRateRules.ToDto(entity, check.Currency!, check.BaseCode)
        };
    }
}

/// <summary>
/// Handles <see cref="UpdateExchangeRateCommand"/>.
/// </summary>
public sealed class UpdateExchangeRateCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<UpdateExchangeRateCommand, Response<ExchangeRateDto>>
{
    public async Task<Response<ExchangeRateDto>> Handle(UpdateExchangeRateCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return ExchangeRateRules.CompanyRequired();
        }

        var companyId = currentUserService.CompanyId;
        var repository = unitOfWork.GetRepository<ExchangeRate>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != companyId || entity.GcRecord != 0)
        {
            return Response<ExchangeRateDto>.Error(FinanceErrorCodes.ExchangeRateRecordNotFound, ["Exchange rate not found."]);
        }

        var check = await ExchangeRateRules.ValidateAsync(unitOfWork, companyId, request, entity.Id);
        if (check.Error is not null)
        {
            return check.Error;
        }

        entity.CurrencyId = request.CurrencyId;
        entity.RateDate = request.RateDate;
        entity.Rate = request.Rate;

        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<ExchangeRateDto>
        {
            IsSuccess = true,
            Message = "Exchange rate updated successfully.",
            Data = ExchangeRateRules.ToDto(entity, check.Currency!, check.BaseCode)
        };
    }
}

/// <summary>
/// Handles <see cref="DeleteExchangeRateCommand"/>.
/// </summary>
public sealed class DeleteExchangeRateCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<DeleteExchangeRateCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(DeleteExchangeRateCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error(FinanceErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var repository = unitOfWork.GetRepository<ExchangeRate>();
        var entity = await repository.GetAsync(request.Id);
        if (entity is null || entity.CompanyId != currentUserService.CompanyId || entity.GcRecord != 0)
        {
            return Response<Guid>.Error(FinanceErrorCodes.ExchangeRateRecordNotFound, ["Exchange rate not found."]);
        }

        entity.MarkAsDeleted();
        await repository.UpdateAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<Guid> { IsSuccess = true, Message = "Exchange rate deleted successfully.", Data = entity.Id };
    }
}

/// <summary>
/// Handles <see cref="RestoreExchangeRateCommand"/> with <see cref="SoftDeleteRestorer"/> (SPEC 41-post).
/// </summary>
public sealed class RestoreExchangeRateCommandHandler(
    ICurrentUserService currentUserService,
    SoftDeleteRestorer restorer)
    : IRequestHandler<RestoreExchangeRateCommand, Response<Guid>>
{
    public async Task<Response<Guid>> Handle(RestoreExchangeRateCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error(FinanceErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!currentUserService.IsInRole("SuperAdmin"))
        {
            return Response<Guid>.Error(FinanceErrorCodes.SuperAdminRequired, ["Only a SuperAdmin can restore deleted records."]);
        }

        return await restorer.RestoreAsync<ExchangeRate>(
            request.Id,
            request.CompanyId,
            activeDuplicateExists: (entity, _) => restorer.AnyAsync<ExchangeRate>(
                x => x.GcRecord == 0 && x.Id != entity.Id && x.CompanyId == entity.CompanyId
                     && x.CurrencyId == entity.CurrencyId && x.RateDate == entity.RateDate),
            cancellationToken: cancellationToken);
    }
}

/// <summary>
/// Rules shared by the exchange rate commands.
/// </summary>
internal static class ExchangeRateRules
{
    internal sealed record Check(Response<ExchangeRateDto>? Error, Currency? Currency, string BaseCode);

    internal static Response<ExchangeRateDto> CompanyRequired() =>
        Response<ExchangeRateDto>.Error(FinanceErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);

    /// <summary>
    /// The currency must be an active, non-base currency of the company (<c>EXCHANGE_RATE_FOR_BASE</c>,
    /// <c>EXCHANGE_RATE_INVALID</c>) and the date must be free for that currency (<c>DUPLICATE_KEY</c>).
    /// </summary>
    internal static async Task<Check> ValidateAsync(IUnitOfWork unitOfWork, Guid companyId, IExchangeRateInput input, Guid? exceptId)
    {
        var companyCurrencies = (await unitOfWork.GetRepository<CompanyCurrency>()
                .GetAllIncludingDeletedAsync(c => c.CompanyId == companyId && c.GcRecord == 0))
            .ToList();

        var baseCurrency = companyCurrencies.FirstOrDefault(c => c.IsBase);
        var baseCode = baseCurrency is null
            ? string.Empty
            : (await unitOfWork.GetRepository<Currency>().GetIncludingDeletedAsync(baseCurrency.CurrencyId))?.Code ?? string.Empty;

        var target = companyCurrencies.FirstOrDefault(c => c.CurrencyId == input.CurrencyId);
        if (target is { IsBase: true })
        {
            return new Check(Response<ExchangeRateDto>.Error(FinanceErrorCodes.ExchangeRateForBase, ["The base currency does not take an exchange rate."]), null, baseCode);
        }

        if (target is null || !target.IsActive)
        {
            return new Check(Response<ExchangeRateDto>.Error(FinanceErrorCodes.ExchangeRateInvalid, ["The currency is not an active currency of the company."]), null, baseCode);
        }

        var duplicate = (await unitOfWork.GetRepository<ExchangeRate>()
                .GetAllIncludingDeletedAsync(r => r.CompanyId == companyId && r.GcRecord == 0
                    && r.CurrencyId == input.CurrencyId && r.RateDate == input.RateDate && r.Id != exceptId))
            .Any();
        if (duplicate)
        {
            return new Check(Response<ExchangeRateDto>.Error(FinanceErrorCodes.DuplicateKey, ["The currency already has a rate for that date."]), null, baseCode);
        }

        var currency = await unitOfWork.GetRepository<Currency>().GetIncludingDeletedAsync(input.CurrencyId);
        return new Check(null, currency, baseCode);
    }

    internal static ExchangeRateDto ToDto(ExchangeRate entity, Currency currency, string baseCode) => new()
    {
        Id = entity.Id,
        GcRecord = entity.GcRecord,
        CompanyId = entity.CompanyId,
        CurrencyId = entity.CurrencyId,
        CurrencyCode = currency.Code,
        BaseCurrencyCode = baseCode,
        RateDate = entity.RateDate,
        Rate = entity.Rate,
        CreatedBy = entity.CreatedBy,
        CreatedAt = entity.Created
    };
}
