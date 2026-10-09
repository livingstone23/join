using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Admin.TaxRegimes.Queries;

public sealed class GetTaxRegimeByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetTaxRegimeByIdQuery, Response<TaxRegimeDto>>
{
    public async Task<Response<TaxRegimeDto>> Handle(GetTaxRegimeByIdQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
            return Response<TaxRegimeDto>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);

        using var connection = connectionFactory.CreateConnection();
        var companyId = TenantResolver.Resolve(currentUserService, request.CompanyId);
        var activeOnly = SoftDeleteVisibility.IncludeDeleted(currentUserService, request.IncludeDeleted)
            ? string.Empty
            : "AND tr.GcRecord = 0";

        var sql = $"""
            SELECT tr.Id, tr.GcRecord, tr.CompanyId, c.Name AS CompanyName, tr.Code, tr.Name, tr.IsActive, tr.Created AS CreatedAt
            FROM Admin.TaxRegimes tr
            INNER JOIN Common.Companies c ON c.Id = tr.CompanyId AND c.GcRecord = 0
            WHERE tr.Id = @Id AND tr.CompanyId = @CompanyId {activeOnly};
            """;

        var item = await connection.QuerySingleOrDefaultAsync<TaxRegimeDto>(
            new CommandDefinition(sql, new { request.Id, CompanyId = companyId }, cancellationToken: cancellationToken));

        if (item is null)
            return Response<TaxRegimeDto>.Error("TAX_REGIME_NOT_FOUND", ["Tax regime not found."]);

        return new Response<TaxRegimeDto> { IsSuccess = true, Message = "Tax regime retrieved successfully.", Data = item };
    }
}
