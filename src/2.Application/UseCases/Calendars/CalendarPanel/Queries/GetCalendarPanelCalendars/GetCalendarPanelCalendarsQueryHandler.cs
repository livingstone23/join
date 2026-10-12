using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Application.UseCases.Calendars.UserCalendars.Queries.GetMyCalendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarPanel.Queries.GetCalendarPanelCalendars;

/// <summary>
/// Handles <see cref="GetCalendarPanelCalendarsQuery"/> with Dapper; the tenant filter keeps other companies out.
/// </summary>
public sealed class GetCalendarPanelCalendarsQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService)
    : IRequestHandler<GetCalendarPanelCalendarsQuery, Response<IReadOnlyList<UserCalendarDto>>>
{
    public async Task<Response<IReadOnlyList<UserCalendarDto>>> Handle(GetCalendarPanelCalendarsQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<IReadOnlyList<UserCalendarDto>>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var userIds = request.UserIds.Distinct().ToArray();
        if (userIds.Length == 0 || userIds.Length > CalendarQueryReader.MaxPanelUsers)
        {
            return Response<IReadOnlyList<UserCalendarDto>>.Error(CalendarErrorCodes.TooManyUsers, [$"Select between 1 and {CalendarQueryReader.MaxPanelUsers} users."]);
        }

        using var connection = connectionFactory.CreateConnection();
        var calendars = (await connection.QueryAsync<UserCalendarDto>(new CommandDefinition(GetMyCalendarsQueryHandler.CalendarsSql,
                new { TenantId = currentUserService.CompanyId, Owners = userIds }, cancellationToken: cancellationToken)))
            .Select(c => c with { OwnerName = c.OwnerName?.Trim() })
            .ToList();

        return new Response<IReadOnlyList<UserCalendarDto>> { IsSuccess = true, Data = calendars, Message = "Calendars retrieved successfully." };
    }
}
