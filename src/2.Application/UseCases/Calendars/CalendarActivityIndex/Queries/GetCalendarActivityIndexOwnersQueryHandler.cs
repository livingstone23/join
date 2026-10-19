using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarActivityIndex.Queries;

/// <summary>
/// SPEC 68 — owners for the activity index filter. Without the per-user flag the list has just the current
/// user, so the dropdown in the UI is collapsed. With the flag the query joins the company's members with
/// their default calendar and trims the result by the free-text term.
/// </summary>
public sealed class GetCalendarActivityIndexOwnersQueryHandler(
    ISqlConnectionFactory connectionFactory,
    CalendarActivityVisibility visibility)
    : IRequestHandler<GetCalendarActivityIndexOwnersQuery, Response<IReadOnlyList<CalendarActivityIndexOwnerItemDto>>>
{
    /// <summary> Maximum per owner request (the dropdown is a small picker). </summary>
    public const int MaxLimit = 50;

    private const string Sql = """
        SELECT u.Id AS UserId,
               CONCAT(u.FirstName, ' ', u.LastName) AS UserName,
               u.Email,
               c.DefaultCalendarId,
               cal.Name AS DefaultCalendarName
        FROM Security.UserCompanies uc
        INNER JOIN Security.Users u ON u.Id = uc.UserId
        INNER JOIN Calendar.UserConfigurations c
            ON c.CompanyId = uc.CompanyId AND c.UserId = uc.UserId AND c.GcRecord = 0
        INNER JOIN Calendar.UserCalendars cal
            ON cal.Id = c.DefaultCalendarId AND cal.GcRecord = 0
        WHERE uc.CompanyId = @CompanyId
          AND u.GcRecord = 0
          AND (u.FirstName ILIKE @Pattern OR u.LastName ILIKE @Pattern OR u.Email ILIKE @Pattern)
        ORDER BY u.FirstName, u.LastName
        LIMIT @Limit;
        """;

    private const string SingleSql = """
        SELECT u.Id AS UserId,
               CONCAT(u.FirstName, ' ', u.LastName) AS UserName,
               u.Email,
               c.DefaultCalendarId,
               cal.Name AS DefaultCalendarName
        FROM Security.Users u
        INNER JOIN Calendar.UserConfigurations c
            ON c.CompanyId = @CompanyId AND c.UserId = u.Id AND c.GcRecord = 0
        INNER JOIN Calendar.UserCalendars cal
            ON cal.Id = c.DefaultCalendarId AND cal.GcRecord = 0
        WHERE u.Id = @UserId
          AND u.GcRecord = 0
        LIMIT 1;
        """;

    public async Task<Response<IReadOnlyList<CalendarActivityIndexOwnerItemDto>>> Handle(
        GetCalendarActivityIndexOwnersQuery request,
        CancellationToken cancellationToken)
    {
        var context = await visibility.ResolveAsync(cancellationToken);
        if (context.CompanyId == Guid.Empty)
        {
            return Response<IReadOnlyList<CalendarActivityIndexOwnerItemDto>>.Error(
                CalendarErrorCodes.CompanyRequired,
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();
        if (!context.CanViewOthersActivities)
        {
            // Without the flag, restrict the picker to the current user; return at most one row.
            var own = await connection.QueryFirstOrDefaultAsync<CalendarActivityIndexOwnerItemDto>(
                new CommandDefinition(SingleSql, new
                {
                    CompanyId = context.CompanyId,
                    UserId = context.CurrentUserId
                }, cancellationToken: cancellationToken));
            var list = own is null
                ? Array.Empty<CalendarActivityIndexOwnerItemDto>()
                : new[] { own };
            return new Response<IReadOnlyList<CalendarActivityIndexOwnerItemDto>>
            {
                IsSuccess = true,
                Message = "Calendar activity index owners retrieved successfully.",
                Data = list
            };
        }

        var limit = Math.Clamp(request.Limit, 1, MaxLimit);
        var pattern = $"%{EscapeLikePattern(request.Search?.Trim() ?? string.Empty)}%";
        var rows = (await connection.QueryAsync<CalendarActivityIndexOwnerItemDto>(
            new CommandDefinition(Sql, new
            {
                CompanyId = context.CompanyId,
                Pattern = pattern,
                Limit = limit
            }, cancellationToken: cancellationToken))).ToList();

        return new Response<IReadOnlyList<CalendarActivityIndexOwnerItemDto>>
        {
            IsSuccess = true,
            Message = "Calendar activity index owners retrieved successfully.",
            Data = rows
        };
    }

    /// <summary>
    /// Escapes <c>\</c>, <c>%</c> and <c>_</c> so the user's text matches literally (PostgreSQL's default LIKE
    /// escape is <c>\</c>).
    /// </summary>
    public static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}