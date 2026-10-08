using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Areas.Queries;

/// <summary>
/// Handles tenant-scoped area detail queries using Dapper.
/// </summary>
/// <param name="connectionFactory">Factory used to create database-agnostic read connections.</param>
public sealed class GetAreaByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetAreaByIdQuery, Response<AreaDto>>
{
    /// <summary>
    /// Retrieves a single active area that belongs to the requested company.
    /// </summary>
    /// <param name="request">The tenant-scoped detail query.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response containing the requested area when it exists.</returns>
    public async Task<Response<AreaDto>> Handle(GetAreaByIdQuery request, CancellationToken cancellationToken)
    {
        // SPEC 41: the X-Company-Id header is only an explicit override, honored for SuperAdmin;
        // every other caller always reads the company of their token (TenantResolver).
        var companyId = TenantResolver.Resolve(currentUserService, request.CompanyId == Guid.Empty ? null : request.CompanyId);
        if (companyId == Guid.Empty)
        {
            return Response<AreaDto>.Error(
                "INVALID_COMPANY_ID",
                ["The X-Company-Id header is required."]);
        }

        using var connection = connectionFactory.CreateConnection();

        var activeOnly = SoftDeleteVisibility.IncludeDeleted(currentUserService, request.IncludeDeleted)
            ? string.Empty
            : "AND a.GcRecord = 0";

        var sql = $"""
            SELECT
                a.Id,
                a.GcRecord,
                a.CompanyId,
                c.Name AS CompanyName,
                a.Name,
                a.EntityStatusId,
                es.Name AS EntityStatusName,
                a.Created
            FROM Admin.Areas a
            INNER JOIN Admin.EntityStatuses es
                ON es.Id = a.EntityStatusId
               AND es.GcRecord = 0
            INNER JOIN Common.Companies c
                ON c.Id = a.CompanyId
               AND c.GcRecord = 0
            WHERE a.Id = @AreaId
              AND a.CompanyId = @CompanyId
              {activeOnly};
            """;

        var area = await connection.QuerySingleOrDefaultAsync<AreaDto>(
            new CommandDefinition(
                sql,
                new { request.AreaId, CompanyId = companyId },
                cancellationToken: cancellationToken));

        if (area is null)
        {
            return Response<AreaDto>.Error("AREA_NOT_FOUND", ["Area not found."]);
        }

        return new Response<AreaDto>
        {
            IsSuccess = true,
            Message = "Area retrieved successfully.",
            Data = area
        };
    }
}
