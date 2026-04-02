using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Common.Countries.Queries;

/// <summary>
/// Handles country detail queries using Dapper for high-performance reads.
/// </summary>
/// <param name="connectionFactory">Factory used to create DB-agnostic read connections.</param>
public class GetCountryByIdQueryHandler(ISqlConnectionFactory connectionFactory)
    : IRequestHandler<GetCountryByIdQuery, Response<CountryDto>>
{
    /// <summary>
    /// Retrieves a country by id.
    /// </summary>
    /// <param name="request">The query payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response containing the country detail.</returns>
    public async Task<Response<CountryDto>> Handle(GetCountryByIdQuery request, CancellationToken cancellationToken)
    {
        using var connection = connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                c.Id,
                c.Name,
                c.IsoCode
            FROM Common.Countries c
            WHERE c.Id = @Id AND c.GcRecord = 0;
            """;

        var parameters = new DynamicParameters();
        parameters.Add("Id", request.CountryId);

        var country = await connection.QuerySingleOrDefaultAsync<CountryDto>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        if (country is null)
        {
            return Response<CountryDto>.Error("COUNTRY_NOT_FOUND", ["Country not found."]);
        }

        return new Response<CountryDto>
        {
            IsSuccess = true,
            Message = "Country retrieved successfully.",
            Data = country
        };
    }
}
