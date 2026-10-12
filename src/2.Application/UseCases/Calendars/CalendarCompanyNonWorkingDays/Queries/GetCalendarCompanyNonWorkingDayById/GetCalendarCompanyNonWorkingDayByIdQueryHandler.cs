using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarCompanyNonWorkingDays.Queries.GetCalendarCompanyNonWorkingDayById;

/// <summary>
/// Handles <see cref="GetCalendarCompanyNonWorkingDayByIdQuery"/> with Dapper.
/// </summary>
public sealed class GetCalendarCompanyNonWorkingDayByIdQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService)
    : IRequestHandler<GetCalendarCompanyNonWorkingDayByIdQuery, Response<CalendarCompanyNonWorkingDayDto>>
{
    private const string Sql = """
        SELECT d.Id, d.Date, d.Name, d.IsRecurringYearly, d.Date AS OccursOn, d.Created AS CreatedAt
        FROM Calendar.CompanyNonWorkingDays d
        WHERE d.CompanyId = @TenantId AND d.GcRecord = 0 AND d.Id = @Id;
        """;

    public async Task<Response<CalendarCompanyNonWorkingDayDto>> Handle(GetCalendarCompanyNonWorkingDayByIdQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<CalendarCompanyNonWorkingDayDto>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();
        var item = await connection.QueryFirstOrDefaultAsync<CalendarCompanyNonWorkingDayDto>(
            new CommandDefinition(Sql, new { TenantId = currentUserService.CompanyId, request.Id }, cancellationToken: cancellationToken));

        return item is null
            ? Response<CalendarCompanyNonWorkingDayDto>.Error(CalendarErrorCodes.NonWorkingDayNotFound, ["The holiday was not found."])
            : new Response<CalendarCompanyNonWorkingDayDto> { IsSuccess = true, Data = item, Message = "Holiday retrieved successfully." };
    }
}
