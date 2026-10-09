using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Admin.IncomeRanges.Queries;

public sealed class GetIncomeRangeByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetIncomeRangeByIdQuery, Response<IncomeRangeDto>>
{
    public async Task<Response<IncomeRangeDto>> Handle(GetIncomeRangeByIdQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
            return Response<IncomeRangeDto>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);

        using var connection = connectionFactory.CreateConnection();
        var companyId = TenantResolver.Resolve(currentUserService, request.CompanyId);
        var activeOnly = SoftDeleteVisibility.IncludeDeleted(currentUserService, request.IncludeDeleted)
            ? string.Empty
            : "AND ir.GcRecord = 0";

        var sql = $"""
            SELECT ir.Id, ir.GcRecord, ir.CompanyId, c.Name AS CompanyName, ir.DisplayName, ir.MinimumValue, ir.MaximumValue, ir.CurrencyCode, ir.IsActive, ir.DisplayOrder, ir.Created AS CreatedAt
            FROM Admin.IncomeRanges ir
            INNER JOIN Common.Companies c ON c.Id = ir.CompanyId AND c.GcRecord = 0
            WHERE ir.Id = @Id AND ir.CompanyId = @CompanyId {activeOnly};
            """;

        var item = await connection.QuerySingleOrDefaultAsync<IncomeRangeDto>(
            new CommandDefinition(sql, new { request.Id, CompanyId = companyId }, cancellationToken: cancellationToken));

        if (item is null)
            return Response<IncomeRangeDto>.Error("INCOME_RANGE_NOT_FOUND", ["Income range not found."]);

        return new Response<IncomeRangeDto> { IsSuccess = true, Message = "Income range retrieved successfully.", Data = item };
    }
}
