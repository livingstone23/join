using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarStatusCompanies.Queries.GetCalendarStatusCompanyById;

/// <summary>
/// Handles <see cref="GetCalendarStatusCompanyByIdQuery"/> with Dapper.
/// </summary>
public sealed class GetCalendarStatusCompanyByIdQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService)
    : IRequestHandler<GetCalendarStatusCompanyByIdQuery, Response<CalendarStatusCompanyDto>>
{
    private static readonly string Sql = $"""
        SELECT {CalendarCompanyCatalogDtos.SelectStatusCompanyColumns}
        WHERE sc.CompanyId = @TenantId AND sc.GcRecord = 0 AND sc.Id = @Id;
        """;

    public async Task<Response<CalendarStatusCompanyDto>> Handle(GetCalendarStatusCompanyByIdQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<CalendarStatusCompanyDto>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();
        var item = await connection.QueryFirstOrDefaultAsync<CalendarStatusCompanyDto>(
            new CommandDefinition(Sql, new { TenantId = currentUserService.CompanyId, request.Id }, cancellationToken: cancellationToken));

        return item is null
            ? Response<CalendarStatusCompanyDto>.Error(CalendarErrorCodes.StatusNotFound, ["The status was not found in the company."])
            : new Response<CalendarStatusCompanyDto> { IsSuccess = true, Data = item, Message = "Company status retrieved successfully." };
    }
}
