using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarCompanies.Queries.GetCurrentCalendarCompany;

/// <summary>
/// Handles <see cref="GetCurrentCalendarCompanyQuery"/> with Dapper.
/// </summary>
public sealed class GetCurrentCalendarCompanyQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService)
    : IRequestHandler<GetCurrentCalendarCompanyQuery, Response<CalendarCompanyDto>>
{
    private const string Sql = """
        SELECT cc.Id,
               cc.DefaultActivityUserId,
               CONCAT(u.FirstName, ' ', u.LastName) AS DefaultActivityUserName,
               cc.TimeZone,
               cc.IsOpen24Hours,
               cc.BusinessHoursStart,
               cc.BusinessHoursEnd,
               cc.DefaultCalendarName,
               cc.RescheduledByOthersStatusCompanyId,
               sc.Name AS RescheduledByOthersStatusName
        FROM Calendar.CalendarCompanies cc
        LEFT JOIN Security.Users u ON u.Id = cc.DefaultActivityUserId
        LEFT JOIN Calendar.StatusCompanies sc ON sc.Id = cc.RescheduledByOthersStatusCompanyId
        WHERE cc.CompanyId = @TenantId AND cc.GcRecord = 0
        LIMIT 1;

        SELECT COUNT(*)
        FROM Admin.CompanyModules cm
        INNER JOIN Admin.SystemModules sm ON sm.Id = cm.ModuleId
        WHERE cm.CompanyId = @TenantId
          AND cm.GcRecord = 0
          AND cm.IsActive = TRUE
          AND sm.GcRecord = 0
          AND sm.IsActive = TRUE
          AND sm.Name = @ModuleName;

        SELECT COUNT(DISTINCT uc.UserId)
        FROM Security.UserCompanies uc
        INNER JOIN Security.Users u ON u.Id = uc.UserId
        WHERE uc.CompanyId = @TenantId
          AND uc.GcRecord = 0
          AND u.GcRecord = 0
          AND u.IsActive = TRUE
          AND NOT EXISTS (
              SELECT 1 FROM Calendar.UserConfigurations c
              WHERE c.CompanyId = @TenantId AND c.UserId = uc.UserId AND c.GcRecord = 0);
        """;

    public async Task<Response<CalendarCompanyDto>> Handle(GetCurrentCalendarCompanyQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<CalendarCompanyDto>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();
        using var multi = await connection.QueryMultipleAsync(new CommandDefinition(Sql,
            new { TenantId = currentUserService.CompanyId, ModuleName = CalendarUserProvisioner.CalendarModuleName },
            cancellationToken: cancellationToken));

        var configuration = await multi.ReadFirstOrDefaultAsync<CalendarCompanyDto>();
        var moduleEnabled = await multi.ReadSingleAsync<int>() > 0;
        var usersWithoutCalendar = await multi.ReadSingleAsync<int>();

        var dto = (configuration ?? new CalendarCompanyDto()) with
        {
            IsConfigured = configuration is not null,
            ModuleEnabled = moduleEnabled,
            UsersWithoutCalendar = usersWithoutCalendar,
            DefaultActivityUserName = configuration?.DefaultActivityUserName?.Trim()
        };

        return new Response<CalendarCompanyDto> { IsSuccess = true, Data = dto, Message = "Calendar configuration retrieved successfully." };
    }
}
