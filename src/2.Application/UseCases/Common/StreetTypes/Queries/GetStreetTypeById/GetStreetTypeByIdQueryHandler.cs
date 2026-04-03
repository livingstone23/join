using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Common.StreetTypes.Queries;

/// <summary>
/// Handles street type detail queries using Dapper.
/// </summary>
/// <param name="connectionFactory">Factory used to create DB-agnostic read connections.</param>
public class GetStreetTypeByIdQueryHandler(ISqlConnectionFactory connectionFactory)
    : IRequestHandler<GetStreetTypeByIdQuery, Response<StreetTypeDto>>
{
    /// <summary>
    /// Retrieves a street type by id.
    /// </summary>
    public async Task<Response<StreetTypeDto>> Handle(GetStreetTypeByIdQuery request, CancellationToken cancellationToken)
    {
        using var connection = connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                st.Id,
                st.Name,
                st.Abbreviation,
                st.IsActive
            FROM Common.StreetTypes st
            WHERE st.Id = @Id AND st.GcRecord = 0;
            """;

        var parameters = new DynamicParameters();
        parameters.Add("Id", request.StreetTypeId);

        var streetType = await connection.QuerySingleOrDefaultAsync<StreetTypeDto>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        if (streetType is null)
        {
            return Response<StreetTypeDto>.Error("STREETTYPE_NOT_FOUND", ["Street type not found."]);
        }

        return new Response<StreetTypeDto>
        {
            IsSuccess = true,
            Message = "Street type retrieved successfully.",
            Data = streetType
        };
    }
}
