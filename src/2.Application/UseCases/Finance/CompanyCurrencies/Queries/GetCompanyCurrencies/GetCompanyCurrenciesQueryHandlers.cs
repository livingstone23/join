using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Finance;
using JOIN.Application.Interface;
using JOIN.Application.Services.Finance;
using MediatR;

namespace JOIN.Application.UseCases.Finance.CompanyCurrencies.Queries;

/// <summary>
/// Shared SQL of the company currency queries. The latest rate comes from a <c>LEFT JOIN LATERAL</c> limited to the
/// newest row on or before <c>@Today</c>; the base never shows a rate.
/// </summary>
internal static class CompanyCurrencyQuerySql
{
    internal const string Select = """
        SELECT
            cc.Id,
            cc.GcRecord,
            cc.CompanyId,
            cc.CurrencyId,
            c.Code,
            c.Name,
            c.Symbol,
            c.Decimals,
            cc.IsBase,
            cc.IsActive,
            CASE WHEN cc.IsBase THEN NULL ELSE lr.Rate END AS LatestRate,
            CASE WHEN cc.IsBase THEN NULL ELSE lr.RateDate END AS LatestRateDate,
            cc.Created AS CreatedAt
        FROM finance.companycurrencies cc
        INNER JOIN finance.currencies c ON c.Id = cc.CurrencyId
        LEFT JOIN LATERAL (
            SELECT r.Rate, r.RateDate
            FROM finance.exchangerates r
            WHERE r.CompanyId = cc.CompanyId
              AND r.CurrencyId = cc.CurrencyId
              AND r.GcRecord = 0
              AND r.RateDate <= @Today
            ORDER BY r.RateDate DESC
            LIMIT 1
        ) lr ON TRUE
        """;
}

/// <summary>
/// Handles <see cref="GetCompanyCurrenciesQuery"/>.
/// </summary>
public sealed class GetCompanyCurrenciesQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetCompanyCurrenciesQuery, Response<IReadOnlyList<CompanyCurrencyDto>>>
{
    public async Task<Response<IReadOnlyList<CompanyCurrencyDto>>> Handle(GetCompanyCurrenciesQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<IReadOnlyList<CompanyCurrencyDto>>.Error(FinanceErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var deleted = SoftDeleteVisibility.IncludeDeleted(currentUserService, request.IncludeDeleted) ? string.Empty : "AND cc.GcRecord = 0";
        var inactive = request.IncludeInactive == false ? "AND cc.IsActive = TRUE" : string.Empty;

        var sql = $"""
            {CompanyCurrencyQuerySql.Select}
            WHERE cc.CompanyId = @CompanyId
              {deleted}
              {inactive}
            ORDER BY cc.IsBase DESC, c.Code;
            """;

        using var connection = connectionFactory.CreateConnection();
        var items = (await connection.QueryAsync<CompanyCurrencyDto>(new CommandDefinition(
                sql,
                new { CompanyId = TenantResolver.Resolve(currentUserService, request.CompanyId), Today = DateTime.UtcNow.Date },
                cancellationToken: cancellationToken)))
            .ToList();

        return new Response<IReadOnlyList<CompanyCurrencyDto>>
        {
            IsSuccess = true,
            Message = "Company currencies retrieved successfully.",
            Data = items
        };
    }
}

/// <summary>
/// Handles <see cref="GetCompanyCurrencyByIdQuery"/>.
/// </summary>
public sealed class GetCompanyCurrencyByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetCompanyCurrencyByIdQuery, Response<CompanyCurrencyDto>>
{
    public async Task<Response<CompanyCurrencyDto>> Handle(GetCompanyCurrencyByIdQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<CompanyCurrencyDto>.Error(FinanceErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var deleted = SoftDeleteVisibility.IncludeDeleted(currentUserService, request.IncludeDeleted) ? string.Empty : "AND cc.GcRecord = 0";
        var sql = $"""
            {CompanyCurrencyQuerySql.Select}
            WHERE cc.Id = @Id
              AND cc.CompanyId = @CompanyId
              {deleted};
            """;

        using var connection = connectionFactory.CreateConnection();
        var item = await connection.QuerySingleOrDefaultAsync<CompanyCurrencyDto>(new CommandDefinition(
            sql,
            new { request.Id, CompanyId = TenantResolver.Resolve(currentUserService, request.CompanyId), Today = DateTime.UtcNow.Date },
            cancellationToken: cancellationToken));

        return item is null
            ? Response<CompanyCurrencyDto>.Error(FinanceErrorCodes.CompanyCurrencyNotFound, ["Company currency not found."])
            : new Response<CompanyCurrencyDto> { IsSuccess = true, Message = "Company currency retrieved successfully.", Data = item };
    }
}
