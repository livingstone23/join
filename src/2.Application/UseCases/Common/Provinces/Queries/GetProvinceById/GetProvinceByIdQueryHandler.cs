using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Common.Provinces.Queries;

/// <summary>
/// Handles province detail queries using Dapper for high-performance reads.
/// </summary>
/// <param name="connectionFactory">Factory used to create database-agnostic read connections.</param>
public sealed class GetProvinceByIdQueryHandler(ISqlConnectionFactory connectionFactory)
    : IRequestHandler<GetProvinceByIdQuery, Response<ProvinceDto>>
{
    /// <summary>
    /// Retrieves a province catalog item by its unique identifier.
    /// </summary>
    /// <param name="request">The query payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response containing the requested province.</returns>
    public async Task<Response<ProvinceDto>> Handle(GetProvinceByIdQuery request, CancellationToken cancellationToken)
    {
        using var connection = connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                p.Id,
                p.Name,
                p.Code,
                p.CountryId,
                c.Name AS CountryName,
                p.RegionId,
                r.Name AS RegionName,
                p.Created AS CreatedAt
            FROM Common.Provinces p
            INNER JOIN Common.Countries c
                ON c.Id = p.CountryId
               AND c.GcRecord = 0
            LEFT JOIN Common.Regions r
                ON r.Id = p.RegionId
               AND r.GcRecord = 0
            WHERE p.Id = @Id
              AND p.GcRecord = 0;
            """;

        var province = await connection.QuerySingleOrDefaultAsync<ProvinceDto>(
            new CommandDefinition(sql, new { request.Id }, cancellationToken: cancellationToken));

        if (province is null)
        {
            return Response<ProvinceDto>.Error("PROVINCE_NOT_FOUND", ["Province not found."]);
        }

        return new Response<ProvinceDto>
        {
            IsSuccess = true,
            Message = "Province retrieved successfully.",
            Data = province
        };
    }
}