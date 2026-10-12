using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.UserCalendars.Queries.GetMyCalendars;

/// <summary>
/// Handles <see cref="GetMyCalendarsQuery"/> with Dapper. Without calendar configuration of the user the list is
/// empty and <c>pendingProvisioning = true</c>.
/// </summary>
public sealed class GetMyCalendarsQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService)
    : IRequestHandler<GetMyCalendarsQuery, Response<MyCalendarsDto>>
{
    public const string CalendarsSql = """
        SELECT uc.Id, uc.OwnerUserId, CONCAT(u.FirstName, ' ', u.LastName) AS OwnerName, uc.Name, uc.Description, uc.Color,
               uc.IsActive, uc.SortOrder, COALESCE(c.DefaultCalendarId = uc.Id, FALSE) AS IsDefault, uc.Created AS CreatedAt
        FROM Calendar.UserCalendars uc
        INNER JOIN Security.Users u ON u.Id = uc.OwnerUserId
        LEFT JOIN Calendar.UserConfigurations c ON c.UserId = uc.OwnerUserId AND c.CompanyId = uc.CompanyId AND c.GcRecord = 0
        WHERE uc.CompanyId = @TenantId
          AND uc.GcRecord = 0
          AND uc.OwnerUserId = ANY(@Owners)
        ORDER BY uc.OwnerUserId, uc.SortOrder, uc.Name;
        """;

    public async Task<Response<MyCalendarsDto>> Handle(GetMyCalendarsQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<MyCalendarsDto>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!Guid.TryParse(currentUserService.UserId, out var userId))
        {
            return Response<MyCalendarsDto>.Error(CalendarErrorCodes.UserRequired, ["The authenticated user could not be resolved."]);
        }

        using var connection = connectionFactory.CreateConnection();
        var calendars = (await connection.QueryAsync<UserCalendarDto>(new CommandDefinition(CalendarsSql,
            new { TenantId = currentUserService.CompanyId, Owners = new[] { userId } }, cancellationToken: cancellationToken)))
            .Select(c => c with { OwnerName = c.OwnerName?.Trim() })
            .ToList();

        return new Response<MyCalendarsDto>
        {
            IsSuccess = true,
            Message = "Calendars retrieved successfully.",
            Data = new MyCalendarsDto { Calendars = calendars, PendingProvisioning = !calendars.Any(c => c.IsDefault) }
        };
    }
}
