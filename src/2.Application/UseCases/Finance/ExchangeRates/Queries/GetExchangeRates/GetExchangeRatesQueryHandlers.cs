using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Finance;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Finance;
using JOIN.Application.Services.Finance;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Finance.ExchangeRates.Queries;

/// <summary>
/// Shared SQL of the exchange rate queries.
/// </summary>
internal static class ExchangeRateQuerySql
{
    internal const string Select = """
        SELECT
            r.Id,
            r.GcRecord,
            r.CompanyId,
            r.CurrencyId,
            c.Code AS CurrencyCode,
            COALESCE(bc.Code, '') AS BaseCurrencyCode,
            r.RateDate,
            r.Rate,
            r.CreatedBy,
            r.Created AS CreatedAt
        FROM finance.exchangerates r
        INNER JOIN finance.currencies c ON c.Id = r.CurrencyId
        LEFT JOIN finance.companycurrencies bcc ON bcc.CompanyId = r.CompanyId AND bcc.IsBase = TRUE AND bcc.GcRecord = 0
        LEFT JOIN finance.currencies bc ON bc.Id = bcc.CurrencyId
        """;
}

/// <summary>
/// Handles <see cref="GetExchangeRatesQuery"/>.
/// </summary>
public sealed class GetExchangeRatesQueryHandler(
    ISqlConnectionFactory connectionFactory,
    IOptions<PaginationSettings> paginationOptions,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetExchangeRatesQuery, Response<PagedResult<ExchangeRateDto>>>
{
    private readonly PaginationSettings _pagination = paginationOptions.Value ?? new();

    public async Task<Response<PagedResult<ExchangeRateDto>>> Handle(GetExchangeRatesQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<PagedResult<ExchangeRateDto>>.Error(FinanceErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var (pageNumber, pageSize) = _pagination.Sanitize(request.PageNumber, request.PageSize);
        var parameters = new DynamicParameters();
        parameters.Add("CompanyId", TenantResolver.Resolve(currentUserService, request.CompanyId));
        parameters.Add("Offset", (pageNumber - 1) * pageSize);
        parameters.Add("PageSize", pageSize);

        var where = new StringBuilder("WHERE r.CompanyId = @CompanyId");
        if (!SoftDeleteVisibility.IncludeDeleted(currentUserService, request.IncludeDeleted))
        {
            where.Append(" AND r.GcRecord = 0");
        }

        if (request.CurrencyId is { } currencyId && currencyId != Guid.Empty)
        {
            where.Append(" AND r.CurrencyId = @CurrencyId");
            parameters.Add("CurrencyId", currencyId);
        }

        if (request.From is { } from)
        {
            where.Append(" AND r.RateDate >= @From");
            parameters.Add("From", from.ToDateTime(TimeOnly.MinValue));
        }

        if (request.To is { } to)
        {
            where.Append(" AND r.RateDate <= @To");
            parameters.Add("To", to.ToDateTime(TimeOnly.MinValue));
        }

        var sql = $"""
            {ExchangeRateQuerySql.Select}
            {where}
            ORDER BY r.RateDate DESC, c.Code
            LIMIT @PageSize OFFSET @Offset;

            SELECT COUNT(*) FROM finance.exchangerates r {where};
            """;

        using var connection = connectionFactory.CreateConnection();
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        var items = (await multi.ReadAsync<ExchangeRateDto>()).AsList();
        var total = await multi.ReadSingleAsync<int>();

        return FinancePaging.Page(items, pageNumber, pageSize, total, "Exchange rates retrieved successfully.");
    }
}

/// <summary>
/// Handles <see cref="GetExchangeRateByIdQuery"/>.
/// </summary>
public sealed class GetExchangeRateByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetExchangeRateByIdQuery, Response<ExchangeRateDto>>
{
    public async Task<Response<ExchangeRateDto>> Handle(GetExchangeRateByIdQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<ExchangeRateDto>.Error(FinanceErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var deleted = SoftDeleteVisibility.IncludeDeleted(currentUserService, request.IncludeDeleted) ? string.Empty : "AND r.GcRecord = 0";
        var sql = $"""
            {ExchangeRateQuerySql.Select}
            WHERE r.Id = @Id
              AND r.CompanyId = @CompanyId
              {deleted};
            """;

        using var connection = connectionFactory.CreateConnection();
        var item = await connection.QuerySingleOrDefaultAsync<ExchangeRateDto>(new CommandDefinition(
            sql,
            new { request.Id, CompanyId = TenantResolver.Resolve(currentUserService, request.CompanyId) },
            cancellationToken: cancellationToken));

        return item is null
            ? Response<ExchangeRateDto>.Error(FinanceErrorCodes.ExchangeRateRecordNotFound, ["Exchange rate not found."])
            : new Response<ExchangeRateDto> { IsSuccess = true, Message = "Exchange rate retrieved successfully.", Data = item };
    }
}

/// <summary>
/// Handles <see cref="ResolveExchangeRateQuery"/> with <see cref="IExchangeRateResolver"/>.
/// </summary>
public sealed class ResolveExchangeRateQueryHandler(
    IExchangeRateResolver resolver,
    ICurrentUserService currentUserService)
    : IRequestHandler<ResolveExchangeRateQuery, Response<ExchangeRateResolutionDto>>
{
    public async Task<Response<ExchangeRateResolutionDto>> Handle(ResolveExchangeRateQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<ExchangeRateResolutionDto>.Error(FinanceErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var date = request.Date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var resolution = await resolver.ResolveAsync(currentUserService.CompanyId, request.CurrencyId, date, cancellationToken);

        return resolution is null
            ? Response<ExchangeRateResolutionDto>.Error(
                FinanceErrorCodes.ExchangeRateNotFound,
                [$"The currency is not an active currency of the company or has no exchange rate on or before {date:yyyy-MM-dd}."])
            : new Response<ExchangeRateResolutionDto>
            {
                IsSuccess = true,
                Message = "Exchange rate resolved successfully.",
                Data = new ExchangeRateResolutionDto
                {
                    CurrencyId = request.CurrencyId,
                    Rate = resolution.Rate,
                    RateDate = resolution.RateDate,
                    IsBase = resolution.IsBase
                }
            };
    }
}
