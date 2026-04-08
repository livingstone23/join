using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Common.Municipalities.Queries;

/// <summary>
/// Handles municipality detail queries using Dapper for high-performance reads.
/// </summary>
/// <param name="connectionFactory">Factory used to create database-agnostic read connections.</param>
public sealed class GetMunicipalityByIdQueryHandler(ISqlConnectionFactory connectionFactory)
    : IRequestHandler<GetMunicipalityByIdQuery, Response<MunicipalityDto>>
{
    /// <summary>
    /// Retrieves a municipality catalog item by its unique identifier.
    /// </summary>
    /// <param name="request">The query payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response containing the requested municipality.</returns>
    public async Task<Response<MunicipalityDto>> Handle(GetMunicipalityByIdQuery request, CancellationToken cancellationToken)
    {
        using var connection = connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                m.Id,
                m.Name,
                m.Code,
                m.ProvinceId,
                p.Name AS ProvinceName,
                m.Created AS CreatedAt
            FROM Common.Municipalities m
            INNER JOIN Common.Provinces p
                ON p.Id = m.ProvinceId
               AND p.GcRecord = 0
            WHERE m.Id = @Id
              AND m.GcRecord = 0;
            """;

        var municipality = await connection.QuerySingleOrDefaultAsync<MunicipalityDto>(
            new CommandDefinition(sql, new { request.Id }, cancellationToken: cancellationToken));

        if (municipality is null)
        {
            return Response<MunicipalityDto>.Error("MUNICIPALITY_NOT_FOUND", ["Municipality not found."]);
        }

        return new Response<MunicipalityDto>
        {
            IsSuccess = true,
            Message = "Municipality retrieved successfully.",
            Data = municipality
        };
    }
}
