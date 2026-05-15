using System.Data;
using System.Text;
using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Admin.Genders.Queries;

/// <summary>
/// Handles tenant-scoped gender list queries using Dapper for high-performance reads.
/// </summary>
public sealed class GetGendersQueryHandler(
    ISqlConnectionFactory connectionFactory,
    IOptions<PaginationSettings> paginationOptions,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetGendersQuery, Response<PagedResult<GenderDto>>>
{
    private readonly PaginationSettings _paginationSettings = paginationOptions.Value ?? new();

    /// <inheritdoc />
    public async Task<Response<PagedResult<GenderDto>>> Handle(GetGendersQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<PagedResult<GenderDto>>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var companyId = currentUserService.CompanyId;
        var defaultPageNumber = _paginationSettings.DefaultPageNumber < 1 ? 1 : _paginationSettings.DefaultPageNumber;
        var defaultPageSize = _paginationSettings.DefaultPageSize < 1 ? 10 : _paginationSettings.DefaultPageSize;
        var maxPageSize = _paginationSettings.MaxPageSize < defaultPageSize ? defaultPageSize : _paginationSettings.MaxPageSize;

        var sanitizedPageNumber = request.PageNumber.GetValueOrDefault(defaultPageNumber);
        sanitizedPageNumber = sanitizedPageNumber < 1 ? defaultPageNumber : sanitizedPageNumber;

        var requestedPageSize = request.PageSize.GetValueOrDefault(defaultPageSize);
        var sanitizedPageSize = requestedPageSize < 1 ? defaultPageSize : Math.Min(requestedPageSize, maxPageSize);
        var offset = (sanitizedPageNumber - 1) * sanitizedPageSize;

        using var connection = connectionFactory.CreateConnection();

        var parameters = new DynamicParameters();
        parameters.Add("CompanyId", companyId);
        parameters.Add("Offset", offset);
        parameters.Add("PageSize", sanitizedPageSize);

        var whereBuilder = new StringBuilder("WHERE g.CompanyId = @CompanyId AND g.GcRecord = 0");

        if (!string.IsNullOrWhiteSpace(request.Code))
        {
            whereBuilder.Append(" AND g.Code LIKE @Code");
            parameters.Add("Code", $"%{request.Code.Trim()}%");
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            whereBuilder.Append(" AND g.Name LIKE @Name");
            parameters.Add("Name", $"%{request.Name.Trim()}%");
        }

        if (request.IsActive.HasValue)
        {
            whereBuilder.Append(" AND g.IsActive = @IsActive");
            parameters.Add("IsActive", request.IsActive.Value);
        }

        var whereClause = whereBuilder.ToString();

        var sql = $"""
            SELECT
                g.Id,
                g.CompanyId,
                c.Name AS CompanyName,
                g.Code,
                g.Name,
                g.IsActive,
                g.Created AS CreatedAt
            FROM Admin.Genders g
            INNER JOIN Common.Companies c
                ON c.Id = g.CompanyId
               AND c.GcRecord = 0
            {whereClause}
            ORDER BY g.Code ASC, g.Name ASC
            {GetPaginationClause(connection)};

            SELECT COUNT(*)
            FROM Admin.Genders g
            INNER JOIN Common.Companies c
                ON c.Id = g.CompanyId
               AND c.GcRecord = 0
            {whereClause};
            """;

        using var multi = await connection.QueryMultipleAsync(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        var items = (await multi.ReadAsync<GenderDto>()).AsList();
        var totalCount = await multi.ReadSingleAsync<int>();

        return new Response<PagedResult<GenderDto>>
        {
            IsSuccess = true,
            Message = "Genders retrieved successfully.",
            Data = new PagedResult<GenderDto>
            {
                Items = items,
                PageNumber = sanitizedPageNumber,
                PageSize = sanitizedPageSize,
                TotalCount = totalCount,
                TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)sanitizedPageSize)
            }
        };
    }

    private static string GetPaginationClause(IDbConnection connection)
        => connection.GetType().Name.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
            ? "LIMIT @PageSize OFFSET @Offset"
            : "OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";
}
