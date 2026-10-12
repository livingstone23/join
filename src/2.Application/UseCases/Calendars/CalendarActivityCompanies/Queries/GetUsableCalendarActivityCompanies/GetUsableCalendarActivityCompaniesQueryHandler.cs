using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarActivityCompanies.Queries.GetUsableCalendarActivityCompanies;

/// <summary>
/// Handles <see cref="GetUsableCalendarActivityCompaniesQuery"/> with Dapper.
/// </summary>
public sealed class GetUsableCalendarActivityCompaniesQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService)
    : IRequestHandler<GetUsableCalendarActivityCompaniesQuery, Response<IReadOnlyList<CalendarActivityCompanyDto>>>
{
    private static readonly string Sql = $"""
        SELECT {CalendarCompanyCatalogDtos.SelectActivityCompanyColumns}
        WHERE ac.CompanyId = @TenantId
          AND ac.GcRecord = 0
          AND ac.IsActive = TRUE
          AND a.GcRecord = 0
          AND a.IsActive = TRUE
        ORDER BY ac.SortOrder, ac.Name;
        """;

    public async Task<Response<IReadOnlyList<CalendarActivityCompanyDto>>> Handle(GetUsableCalendarActivityCompaniesQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<IReadOnlyList<CalendarActivityCompanyDto>>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();
        var items = (await connection.QueryAsync<CalendarActivityCompanyDto>(
            new CommandDefinition(Sql, new { TenantId = currentUserService.CompanyId }, cancellationToken: cancellationToken))).AsList();

        return new Response<IReadOnlyList<CalendarActivityCompanyDto>> { IsSuccess = true, Data = items, Message = "Usable activity types retrieved successfully." };
    }
}
