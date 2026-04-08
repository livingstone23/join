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
public sealed class GetAreaByIdQueryHandler(ISqlConnectionFactory connectionFactory)
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
        if (request.CompanyId == Guid.Empty)
        {
            return Response<AreaDto>.Error(
                "INVALID_COMPANY_ID",
                ["The X-Company-Id header is required."]);
        }

        using var connection = connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                a.Id,
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
              AND a.GcRecord = 0;
            """;

        var area = await connection.QuerySingleOrDefaultAsync<AreaDto>(
            new CommandDefinition(
                sql,
                new { request.AreaId, request.CompanyId },
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
