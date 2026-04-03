using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Common.CommunicationChannels.Queries;

/// <summary>
/// Handles communication channel detail queries using Dapper.
/// </summary>
/// <param name="connectionFactory">Factory used to create DB-agnostic read connections.</param>
public class GetCommunicationChannelByIdQueryHandler(ISqlConnectionFactory connectionFactory)
    : IRequestHandler<GetCommunicationChannelByIdQuery, Response<CommunicationChannelDto>>
{
    /// <summary>
    /// Retrieves a communication channel by id.
    /// </summary>
    public async Task<Response<CommunicationChannelDto>> Handle(GetCommunicationChannelByIdQuery request, CancellationToken cancellationToken)
    {
        using var connection = connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                cc.Id,
                cc.Name,
                cc.Provider,
                cc.Code,
                cc.IsActive
            FROM Common.CommunicationChannels cc
            WHERE cc.Id = @Id AND cc.GcRecord = 0;
            """;

        var parameters = new DynamicParameters();
        parameters.Add("Id", request.CommunicationChannelId);

        var channel = await connection.QuerySingleOrDefaultAsync<CommunicationChannelDto>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken));

        if (channel is null)
        {
            return Response<CommunicationChannelDto>.Error("COMMUNICATIONCHANNEL_NOT_FOUND", ["Communication channel not found."]);
        }

        return new Response<CommunicationChannelDto>
        {
            IsSuccess = true,
            Message = "Communication channel retrieved successfully.",
            Data = channel
        };
    }
}
