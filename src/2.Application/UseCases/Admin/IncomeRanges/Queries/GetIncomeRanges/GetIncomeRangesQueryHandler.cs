using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Admin.IncomeRanges.Queries;

public sealed class GetIncomeRangesQueryHandler(
    ISqlConnectionFactory connectionFactory,
    IOptions<PaginationSettings> paginationOptions,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetIncomeRangesQuery, Response<PagedResult<IncomeRangeDto>>>
{
    private readonly PaginationSettings _pagination = paginationOptions.Value ?? new();

    public async Task<Response<PagedResult<IncomeRangeDto>>> Handle(GetIncomeRangesQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
            return Response<PagedResult<IncomeRangeDto>>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);

        var companyId = currentUserService.CompanyId;
        var pageNumber = Math.Max(request.PageNumber ?? _pagination.DefaultPageNumber, 1);
        var pageSize = Math.Min(Math.Max(request.PageSize ?? _pagination.DefaultPageSize, 1), _pagination.MaxPageSize);
        var offset = (pageNumber - 1) * pageSize;

        using var connection = connectionFactory.CreateConnection();
        var parameters = new DynamicParameters();
        parameters.Add("CompanyId", companyId);
        parameters.Add("Offset", offset);
        parameters.Add("PageSize", pageSize);

        var where = new StringBuilder("WHERE ir.CompanyId = @CompanyId AND ir.GcRecord = 0");
        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            where.Append(" AND ir.DisplayName LIKE @DisplayName");
            parameters.Add("DisplayName", $"%{request.DisplayName.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(request.CurrencyCode))
        {
            where.Append(" AND ir.CurrencyCode = @CurrencyCode");
            parameters.Add("CurrencyCode", request.CurrencyCode.Trim().ToUpperInvariant());
        }

        if (request.IsActive.HasValue)
        {
            where.Append(" AND ir.IsActive = @IsActive");
            parameters.Add("IsActive", request.IsActive.Value);
        }

        var whereClause = where.ToString();
        var pagination = connection.GetType().Name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
            ? "LIMIT @PageSize OFFSET @Offset"
            : "OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

        var sql = $"""
            SELECT ir.Id, ir.CompanyId, c.Name AS CompanyName, ir.DisplayName, ir.MinimumValue, ir.MaximumValue, ir.CurrencyCode, ir.IsActive, ir.Created AS CreatedAt
            FROM Admin.IncomeRanges ir
            INNER JOIN Common.Companies c ON c.Id = ir.CompanyId AND c.GcRecord = 0
            {whereClause} ORDER BY ir.DisplayName ASC {pagination};
            SELECT COUNT(*) FROM Admin.IncomeRanges ir
            INNER JOIN Common.Companies c ON c.Id = ir.CompanyId AND c.GcRecord = 0
            {whereClause};
            """;

        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        var items = (await multi.ReadAsync<IncomeRangeDto>()).AsList();
        var total = await multi.ReadSingleAsync<int>();

        return new Response<PagedResult<IncomeRangeDto>>
        {
            IsSuccess = true,
            Message = "Income ranges retrieved successfully.",
            Data = new PagedResult<IncomeRangeDto>
            {
                Items = items,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalCount = total,
                TotalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize)
            }
        };
    }
}
