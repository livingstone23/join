using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake.GetIntakeIdentificationTypes;

/// <summary>
/// Active global identification types. The catalog has no tenant scope so the query is straightforward: any
/// authenticated user with the intake permission can list them.
/// </summary>
public sealed class GetIntakeIdentificationTypesQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetIntakeIdentificationTypesQuery, Response<IReadOnlyList<IntakeIdentificationTypeDto>>>
{
    private const string Sql = """
        SELECT id, name
          FROM admin.identificationtypes
         WHERE gcrecord = 0 AND isactive = TRUE
         ORDER BY name;
        """;

    public async Task<Response<IReadOnlyList<IntakeIdentificationTypeDto>>> Handle(GetIntakeIdentificationTypesQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<IReadOnlyList<IntakeIdentificationTypeDto>>.Error(CalendarErrorCodes.CompanyRequired,
                ["The X-Company-Id header or the CompanyId claim is required."]);
        }

        using var connection = connectionFactory.CreateConnection();
        var rows = (await connection.QueryAsync<IntakeIdentificationTypeDto>(
            new CommandDefinition(Sql, cancellationToken: cancellationToken)))
            .AsList();

        return new Response<IReadOnlyList<IntakeIdentificationTypeDto>>
        {
            IsSuccess = true,
            Data = rows,
            Message = "Identification types retrieved successfully."
        };
    }
}