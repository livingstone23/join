using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarActivities.Queries.GetCalendarActivityById;

/// <summary>
/// Handles <see cref="GetCalendarActivityByIdQuery"/> with Dapper.
/// </summary>
public sealed class GetCalendarActivityByIdQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService)
    : IRequestHandler<GetCalendarActivityByIdQuery, Response<CalendarActivityDto>>
{
    private const string Sql = """
        SELECT a.Id, a.Code, a.Name, a.Description, a.Color, a.Icon, a.DefaultDurationMinutes,
               a.AllowsOverlap, a.RequiresBusinessHours, a.IsActive, a.SortOrder, a.Created AS CreatedAt
        FROM Calendar.Activities a
        WHERE a.Id = @Id AND a.GcRecord = 0;
        """;

    public async Task<Response<CalendarActivityDto>> Handle(GetCalendarActivityByIdQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<CalendarActivityDto>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();
        var item = await connection.QueryFirstOrDefaultAsync<CalendarActivityDto>(
            new CommandDefinition(Sql, new { request.Id }, cancellationToken: cancellationToken));

        return item is null
            ? Response<CalendarActivityDto>.Error(CalendarErrorCodes.ActivityNotFound, ["The activity type was not found."])
            : new Response<CalendarActivityDto> { IsSuccess = true, Data = item, Message = "Calendar activity type retrieved successfully." };
    }
}
