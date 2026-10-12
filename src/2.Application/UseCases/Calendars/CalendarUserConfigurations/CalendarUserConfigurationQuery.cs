using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;

namespace JOIN.Application.UseCases.Calendars.CalendarUserConfigurations;

/// <summary>
/// Dapper read of one user's configuration in the current company (shared by <c>/me</c> and <c>/{userId}</c>).
/// Requires the company calendar configuration (R1); a member without calendar answers <c>hasCalendar = false</c>.
/// </summary>
public sealed class CalendarUserConfigurationQuery(
    ISqlConnectionFactory connectionFactory,
    CalendarQueryReader reader,
    ICurrentUserService currentUserService)
{
    private static readonly string Sql = $"""
        SELECT {CalendarUserConfigurationDtos.SelectColumns}
        WHERE uc.CompanyId = @TenantId
          AND uc.GcRecord = 0
          AND u.GcRecord = 0
          AND uc.UserId = @UserId
        LIMIT 1;
        """;

    public async Task<Response<CalendarUserConfigurationDto>> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<CalendarUserConfigurationDto>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();
        var company = await reader.GetCalendarCompanyAsync(connection, companyId, cancellationToken);
        if (company is null)
        {
            return Response<CalendarUserConfigurationDto>.Error(CalendarErrorCodes.CompanyNotConfigured, ["The company has no calendar configuration."]);
        }

        var row = await connection.QueryFirstOrDefaultAsync<CalendarUserConfigurationDto>(
            new CommandDefinition(Sql, new { TenantId = companyId, UserId = userId }, cancellationToken: cancellationToken));
        if (row is null)
        {
            return Response<CalendarUserConfigurationDto>.Error(CalendarErrorCodes.UserNotInCompany, ["The user is not an active user of the current company."]);
        }

        return new Response<CalendarUserConfigurationDto>
        {
            IsSuccess = true,
            Data = CalendarUserConfigurationDtos.Enrich(row, company),
            Message = "Calendar configuration retrieved successfully."
        };
    }
}
