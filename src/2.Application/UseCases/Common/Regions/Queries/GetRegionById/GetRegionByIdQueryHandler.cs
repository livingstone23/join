using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Common.Regions.Queries;

/// <summary>
/// Handles region detail queries using Dapper for high-performance reads.
/// SPEC 38: tenant-scoped — a Manager of company A cannot read a region of company B by id.
/// </summary>
/// <param name="connectionFactory">Factory used to create database-agnostic read connections.</param>
/// <param name="currentUserService">Resolves the active tenant for the region filter.</param>
public sealed class GetRegionByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetRegionByIdQuery, Response<RegionDto>>
{
    /// <summary>
    /// Retrieves a region catalog item by its unique identifier.
    /// </summary>
    /// <param name="request">The query payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response containing the requested region.</returns>
    public async Task<Response<RegionDto>> Handle(GetRegionByIdQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<RegionDto>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();

        var activeOnly = SoftDeleteVisibility.IncludeDeleted(currentUserService, request.IncludeDeleted)
            ? string.Empty
            : "AND r.GcRecord = 0";

        var sql = $"""
            SELECT
                r.Id,
                r.GcRecord,
                r.Name,
                r.Code,
                r.CountryId,
                c.Name AS CountryName,
                r.Created AS CreatedAt
            FROM Admin.Regions r
            INNER JOIN Common.Countries c
                ON c.Id = r.CountryId
               AND c.GcRecord = 0
            WHERE r.Id = @Id
              AND r.CompanyId = @TenantId
              {activeOnly};
            """;

        var region = await connection.QuerySingleOrDefaultAsync<RegionDto>(
            new CommandDefinition(
                sql,
                new { request.Id, TenantId = TenantResolver.Resolve(currentUserService, request.CompanyId) },
                cancellationToken: cancellationToken));

        if (region is null)
        {
            return Response<RegionDto>.Error("REGION_NOT_FOUND", ["Region not found."]);
        }

        return new Response<RegionDto>
        {
            IsSuccess = true,
            Message = "Region retrieved successfully.",
            Data = region
        };
    }
}
