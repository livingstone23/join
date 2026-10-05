using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Common.Companies.Queries;

/// <summary>
/// Handles company detail queries using Dapper.
/// SPEC 38: a SuperAdminCompany of A must NOT read company B; we return NOT_FOUND when the id
/// doesn't match the token's CompanyId and the caller is not a real SuperAdmin.
/// </summary>
/// <param name="connectionFactory">Factory used to create DB-agnostic read connections.</param>
/// <param name="currentUserService">Resolves the caller's tenant and role for the tenant-scope guard.</param>
public class GetCompanyByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetCompanyByIdQuery, Response<CompanyDto>>
{
    /// <summary>
    /// Retrieves a company by id.
    /// </summary>
    public async Task<Response<CompanyDto>> Handle(GetCompanyByIdQuery request, CancellationToken cancellationToken)
    {
        // SPEC 38: non-SuperAdmin can only read its own tenant. Returning NOT_FOUND (instead of
        // 403) hides the existence of other tenants — same response a deleted company would yield.
        var isSuperAdmin = currentUserService.IsInRole("SuperAdmin");
        if (!isSuperAdmin)
        {
            var tenantId = currentUserService.CompanyId;
            if (tenantId == Guid.Empty || tenantId != request.CompanyId)
            {
                return Response<CompanyDto>.Error("COMPANY_NOT_FOUND", ["Company not found."]);
            }
        }

        using var connection = connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                c.Id,
                c.Name,
                c.Description,
                c.TaxId,
                c.Email,
                c.Phone,
                c.WebSite,
                c.IsActive
            FROM Common.Companies c
            WHERE c.Id = @Id AND c.GcRecord = 0;
            """;

        var parameters = new DynamicParameters();
        parameters.Add("Id", request.CompanyId);

        var company = await connection.QuerySingleOrDefaultAsync<CompanyDto>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        if (company is null)
        {
            return Response<CompanyDto>.Error("COMPANY_NOT_FOUND", ["Company not found."]);
        }

        return new Response<CompanyDto>
        {
            IsSuccess = true,
            Message = "Company retrieved successfully.",
            Data = company
        };
    }
}
