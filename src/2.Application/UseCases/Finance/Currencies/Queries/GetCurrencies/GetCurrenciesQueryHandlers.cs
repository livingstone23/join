using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Finance;
using JOIN.Application.Interface;
using JOIN.Application.Services.Finance;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Finance.Currencies.Queries;

/// <summary>
/// Shared SQL of the global currency queries.
/// </summary>
internal static class CurrencyQuerySql
{
    internal const string Select = """
        SELECT
            c.Id,
            c.GcRecord,
            c.Code,
            c.Name,
            c.Symbol,
            c.Decimals,
            c.IsActive,
            c.Created AS CreatedAt,
            (SELECT COUNT(*)::int
             FROM finance.companycurrencies cc
             WHERE cc.CurrencyId = c.Id
               AND cc.GcRecord = 0) AS CompaniesCount
        FROM finance.currencies c
        """;
}

/// <summary>
/// Handles <see cref="GetCurrenciesQuery"/>.
/// </summary>
public sealed class GetCurrenciesQueryHandler(
    ISqlConnectionFactory connectionFactory,
    IOptions<PaginationSettings> paginationOptions,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetCurrenciesQuery, Response<PagedResult<CurrencyDto>>>
{
    private readonly PaginationSettings _pagination = paginationOptions.Value ?? new();

    public async Task<Response<PagedResult<CurrencyDto>>> Handle(GetCurrenciesQuery request, CancellationToken cancellationToken)
    {
        var (pageNumber, pageSize) = _pagination.Sanitize(request.PageNumber, request.PageSize);
        var parameters = new DynamicParameters();
        parameters.Add("Offset", (pageNumber - 1) * pageSize);
        parameters.Add("PageSize", pageSize);

        var where = new StringBuilder(SoftDeleteVisibility.IncludeDeleted(currentUserService, request.IncludeDeleted)
            ? "WHERE 1 = 1"
            : "WHERE c.GcRecord = 0");

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            where.Append(" AND (c.Code ILIKE @Search OR c.Name ILIKE @Search)");
            parameters.Add("Search", $"%{request.Search.Trim()}%");
        }

        if (request.IsActive.HasValue)
        {
            where.Append(" AND c.IsActive = @IsActive");
            parameters.Add("IsActive", request.IsActive.Value);
        }

        var sql = $"""
            {CurrencyQuerySql.Select}
            {where}
            ORDER BY c.Code
            LIMIT @PageSize OFFSET @Offset;

            SELECT COUNT(*) FROM finance.currencies c {where};
            """;

        using var connection = connectionFactory.CreateConnection();
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        var items = (await multi.ReadAsync<CurrencyDto>()).AsList();
        var total = await multi.ReadSingleAsync<int>();

        return FinancePaging.Page(items, pageNumber, pageSize, total, "Currencies retrieved successfully.");
    }
}

/// <summary>
/// Handles <see cref="GetCurrencyByIdQuery"/>.
/// </summary>
public sealed class GetCurrencyByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetCurrencyByIdQuery, Response<CurrencyDto>>
{
    public async Task<Response<CurrencyDto>> Handle(GetCurrencyByIdQuery request, CancellationToken cancellationToken)
    {
        var deleted = SoftDeleteVisibility.IncludeDeleted(currentUserService, request.IncludeDeleted) ? string.Empty : "AND c.GcRecord = 0";
        var sql = $"""
            {CurrencyQuerySql.Select}
            WHERE c.Id = @Id
              {deleted};
            """;

        using var connection = connectionFactory.CreateConnection();
        var currency = await connection.QuerySingleOrDefaultAsync<CurrencyDto>(new CommandDefinition(
            sql, new { request.Id }, cancellationToken: cancellationToken));

        return currency is null
            ? Response<CurrencyDto>.Error(FinanceErrorCodes.CurrencyNotFound, ["Currency not found."])
            : new Response<CurrencyDto> { IsSuccess = true, Message = "Currency retrieved successfully.", Data = currency };
    }
}
