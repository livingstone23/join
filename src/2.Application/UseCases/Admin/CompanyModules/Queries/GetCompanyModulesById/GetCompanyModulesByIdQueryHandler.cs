using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Admin.CompanyModules.Queries;

/// <summary>
/// Handles tenant-scoped company module detail queries using Dapper for high-performance reads.
/// </summary>
/// <param name="connectionFactory">Factory used to create database-agnostic read connections.</param>
public sealed class GetCompanyModulesByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetCompanyModulesByIdQuery, Response<CompanyModuleDto>>
{
    /// <summary>
    /// Retrieves a single active company module assignment for the requested tenant.
    /// </summary>
    /// <param name="request">The tenant-scoped detail query.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response containing the matching assignment when it exists.</returns>
    public async Task<Response<CompanyModuleDto>> Handle(GetCompanyModulesByIdQuery request, CancellationToken cancellationToken)
    {
        // SPEC 43: a SuperAdmin reads any company's assignment; anybody else only the ones of their own company.
        var isSuperAdmin = currentUserService.IsInRole("SuperAdmin");
        var companyId = isSuperAdmin ? request.CompanyId : currentUserService.CompanyId;

        if (!isSuperAdmin && companyId == Guid.Empty)
        {
            return Response<CompanyModuleDto>.Error(
                "INVALID_COMPANY_ID",
                ["CompanyId query parameter is required."]);
        }

        using var connection = connectionFactory.CreateConnection();

        var activeOnly = SoftDeleteVisibility.IncludeDeleted(currentUserService, request.IncludeDeleted)
            ? string.Empty
            : "AND cm.GcRecord = 0";

        var companyOnly = companyId.HasValue ? "AND cm.CompanyId = @CompanyId" : string.Empty;

        var sql = $"""
            SELECT
                cm.Id,
                cm.GcRecord,
                cm.CompanyId,
                c.Name AS CompanyName,
                cm.ModuleId,
                sm.Name AS ModuleName,
                sm.IsBase,
                cm.IsActive,
                cm.Created AS CreatedAt
            FROM Admin.CompanyModules cm
            INNER JOIN Common.Companies c
                ON c.Id = cm.CompanyId
               AND c.GcRecord = 0
            INNER JOIN Admin.SystemModules sm
                ON sm.Id = cm.ModuleId
               AND sm.GcRecord = 0
            WHERE cm.Id = @Id
              {companyOnly}
              {activeOnly};
            """;

        var entity = await connection.QuerySingleOrDefaultAsync<CompanyModuleDto>(
            new CommandDefinition(sql, new { request.Id, CompanyId = companyId }, cancellationToken: cancellationToken));

        if (entity is null)
        {
            return Response<CompanyModuleDto>.Error(
                "COMPANY_MODULE_NOT_FOUND",
                ["Company module not found."]);
        }

        return new Response<CompanyModuleDto>
        {
            IsSuccess = true,
            Message = "Company module retrieved successfully.",
            Data = entity
        };
    }
}
