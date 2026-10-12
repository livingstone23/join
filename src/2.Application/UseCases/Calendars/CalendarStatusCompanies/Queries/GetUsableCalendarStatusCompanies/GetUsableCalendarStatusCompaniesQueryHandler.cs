using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarStatusCompanies.Queries.GetUsableCalendarStatusCompanies;

/// <summary>
/// Handles <see cref="GetUsableCalendarStatusCompaniesQuery"/> with Dapper.
/// </summary>
public sealed class GetUsableCalendarStatusCompaniesQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService)
    : IRequestHandler<GetUsableCalendarStatusCompaniesQuery, Response<IReadOnlyList<CalendarStatusCompanyDto>>>
{
    private static readonly string Sql = $"""
        SELECT {CalendarCompanyCatalogDtos.SelectStatusCompanyColumns}
        WHERE sc.CompanyId = @TenantId
          AND sc.GcRecord = 0
          AND sc.IsActive = TRUE
          AND s.GcRecord = 0
          AND s.IsActive = TRUE
        ORDER BY sc.SortOrder, sc.Name;
        """;

    public async Task<Response<IReadOnlyList<CalendarStatusCompanyDto>>> Handle(GetUsableCalendarStatusCompaniesQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<IReadOnlyList<CalendarStatusCompanyDto>>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();
        var items = (await connection.QueryAsync<CalendarStatusCompanyDto>(
            new CommandDefinition(Sql, new { TenantId = currentUserService.CompanyId }, cancellationToken: cancellationToken))).AsList();

        return new Response<IReadOnlyList<CalendarStatusCompanyDto>> { IsSuccess = true, Data = items, Message = "Usable statuses retrieved successfully." };
    }
}
