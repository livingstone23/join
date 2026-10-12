using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarStatuses.Queries.GetCalendarStatusById;

/// <summary>
/// Handles <see cref="GetCalendarStatusByIdQuery"/> with Dapper.
/// </summary>
public sealed class GetCalendarStatusByIdQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService)
    : IRequestHandler<GetCalendarStatusByIdQuery, Response<CalendarStatusDto>>
{
    private const string Sql = """
        SELECT s.Id, s.Code, s.Name, s.Description, s.Color, s.BlocksTime, s.IsFinal, s.IsActive, s.SortOrder, s.Created AS CreatedAt
        FROM Calendar.Statuses s
        WHERE s.Id = @Id AND s.GcRecord = 0;
        """;

    public async Task<Response<CalendarStatusDto>> Handle(GetCalendarStatusByIdQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<CalendarStatusDto>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();
        var item = await connection.QueryFirstOrDefaultAsync<CalendarStatusDto>(
            new CommandDefinition(Sql, new { request.Id }, cancellationToken: cancellationToken));

        return item is null
            ? Response<CalendarStatusDto>.Error(CalendarErrorCodes.StatusNotFound, ["The status was not found."])
            : new Response<CalendarStatusDto> { IsSuccess = true, Data = item, Message = "Calendar status retrieved successfully." };
    }
}
