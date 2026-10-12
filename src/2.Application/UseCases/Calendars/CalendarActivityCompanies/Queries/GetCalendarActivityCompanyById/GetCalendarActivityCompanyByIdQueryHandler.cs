using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarActivityCompanies.Queries.GetCalendarActivityCompanyById;

/// <summary>
/// Handles <see cref="GetCalendarActivityCompanyByIdQuery"/> with Dapper.
/// </summary>
public sealed class GetCalendarActivityCompanyByIdQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService)
    : IRequestHandler<GetCalendarActivityCompanyByIdQuery, Response<CalendarActivityCompanyDto>>
{
    private static readonly string Sql = $"""
        SELECT {CalendarCompanyCatalogDtos.SelectActivityCompanyColumns}
        WHERE ac.CompanyId = @TenantId AND ac.GcRecord = 0 AND ac.Id = @Id;
        """;

    public async Task<Response<CalendarActivityCompanyDto>> Handle(GetCalendarActivityCompanyByIdQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<CalendarActivityCompanyDto>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();
        var item = await connection.QueryFirstOrDefaultAsync<CalendarActivityCompanyDto>(
            new CommandDefinition(Sql, new { TenantId = currentUserService.CompanyId, request.Id }, cancellationToken: cancellationToken));

        return item is null
            ? Response<CalendarActivityCompanyDto>.Error(CalendarErrorCodes.ActivityNotFound, ["The activity type was not found in the company."])
            : new Response<CalendarActivityCompanyDto> { IsSuccess = true, Data = item, Message = "Company activity type retrieved successfully." };
    }
}
