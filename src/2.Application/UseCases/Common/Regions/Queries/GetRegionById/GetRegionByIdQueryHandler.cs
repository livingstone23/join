using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Common.Regions.Queries;

/// <summary>
/// Handles region detail queries using Dapper for high-performance reads.
/// </summary>
/// <param name="connectionFactory">Factory used to create database-agnostic read connections.</param>
public sealed class GetRegionByIdQueryHandler(ISqlConnectionFactory connectionFactory)
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
        using var connection = connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                r.Id,
                r.Name,
                r.Code,
                r.CountryId,
                c.Name AS CountryName,
                r.Created AS CreatedAt
            FROM Common.Regions r
            INNER JOIN Common.Countries c
                ON c.Id = r.CountryId
               AND c.GcRecord = 0
            WHERE r.Id = @Id
              AND r.GcRecord = 0;
            """;

        var region = await connection.QuerySingleOrDefaultAsync<RegionDto>(
            new CommandDefinition(sql, new { request.Id }, cancellationToken: cancellationToken));

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
