using System.Data;
using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Admin.TaxRegimes.Queries;

public sealed class GetTaxRegimesQueryHandler(
    ISqlConnectionFactory connectionFactory,
    IOptions<PaginationSettings> paginationOptions,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetTaxRegimesQuery, Response<PagedResult<TaxRegimeDto>>>
{
    private readonly PaginationSettings _pagination = paginationOptions.Value ?? new();

    public async Task<Response<PagedResult<TaxRegimeDto>>> Handle(GetTaxRegimesQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
            return Response<PagedResult<TaxRegimeDto>>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);

        var companyId = currentUserService.CompanyId;
        var pageNumber = Math.Max(request.PageNumber ?? _pagination.DefaultPageNumber, 1);
        var pageSize = Math.Min(Math.Max(request.PageSize ?? _pagination.DefaultPageSize, 1), _pagination.MaxPageSize);
        var offset = (pageNumber - 1) * pageSize;

        using var connection = connectionFactory.CreateConnection();
        var parameters = new DynamicParameters();
        parameters.Add("CompanyId", companyId);
        parameters.Add("Offset", offset);
        parameters.Add("PageSize", pageSize);

        var where = new StringBuilder("WHERE tr.CompanyId = @CompanyId AND tr.GcRecord = 0");
        if (!string.IsNullOrWhiteSpace(request.Code)) { where.Append(" AND tr.Code LIKE @Code"); parameters.Add("Code", $"%{request.Code.Trim()}%"); }
        if (!string.IsNullOrWhiteSpace(request.Name)) { where.Append(" AND tr.Name LIKE @Name"); parameters.Add("Name", $"%{request.Name.Trim()}%"); }
        if (request.IsActive.HasValue) { where.Append(" AND tr.IsActive = @IsActive"); parameters.Add("IsActive", request.IsActive.Value); }

        var whereClause = where.ToString();
        var pagination = connection.GetType().Name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
            ? "LIMIT @PageSize OFFSET @Offset" : "OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

        var sql = $"""
            SELECT tr.Id, tr.CompanyId, c.Name AS CompanyName, tr.Code, tr.Name, tr.IsActive, tr.Created AS CreatedAt
            FROM Admin.TaxRegimes tr
            INNER JOIN Common.Companies c ON c.Id = tr.CompanyId AND c.GcRecord = 0
            {whereClause} ORDER BY tr.Code ASC, tr.Name ASC {pagination};
            SELECT COUNT(*) FROM Admin.TaxRegimes tr
            INNER JOIN Common.Companies c ON c.Id = tr.CompanyId AND c.GcRecord = 0
            {whereClause};
            """;

        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));
        var items = (await multi.ReadAsync<TaxRegimeDto>()).AsList();
        var total = await multi.ReadSingleAsync<int>();

        return new Response<PagedResult<TaxRegimeDto>>
        {
            IsSuccess = true,
            Message = "Tax regimes retrieved successfully.",
            Data = new PagedResult<TaxRegimeDto>
            {
                Items = items, PageNumber = pageNumber, PageSize = pageSize, TotalCount = total,
                TotalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize)
            }
        };
    }
}
