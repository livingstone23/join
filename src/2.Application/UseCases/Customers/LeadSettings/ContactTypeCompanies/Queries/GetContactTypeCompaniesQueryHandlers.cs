using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.LeadSettings;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Customers.LeadSettings.Common;
using MediatR;

namespace JOIN.Application.UseCases.Customers.LeadSettings.ContactTypeCompanies.Queries;

internal static class ContactTypeCompanyQuerySql
{
    internal const string Select = """
        SELECT
            c.Id,
            c.CompanyId,
            c.ContactTypeId,
            t.Code,
            c.Name,
            t.Name AS MasterName,
            t.Icon,
            t.CommunicationChannelCode,
            t.SchedulesCalendarEvent,
            t.AsksDuration,
            c.SortOrder,
            c.IsActive,
            c.CalendarActivityCompanyId
        FROM customer.contacttypecompanies c
        INNER JOIN customer.contacttypes t ON t.Id = c.ContactTypeId AND t.GcRecord = 0
        """;
}

/// <summary>Handles <see cref="GetContactTypeCompaniesQuery"/>.</summary>
public sealed class GetContactTypeCompaniesQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService)
    : IRequestHandler<GetContactTypeCompaniesQuery, Response<IReadOnlyList<ContactTypeCompanyDto>>>
{
    public async Task<Response<IReadOnlyList<ContactTypeCompanyDto>>> Handle(GetContactTypeCompaniesQuery request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return CommercialCatalogQuery.CompanyRequired<IReadOnlyList<ContactTypeCompanyDto>>();
        }

        var inactive = request.IncludeInactive == false ? "AND c.IsActive = TRUE AND t.IsActive = TRUE" : string.Empty;
        var sql = $"""
            {ContactTypeCompanyQuerySql.Select}
            WHERE c.CompanyId = @CompanyId
              AND c.GcRecord = 0
              {inactive}
            ORDER BY c.SortOrder, c.Name;
            """;

        using var connection = connectionFactory.CreateConnection();
        var items = (await connection.QueryAsync<ContactTypeCompanyDto>(new CommandDefinition(
                sql, new { CompanyId = TenantResolver.Resolve(currentUserService, request.CompanyId) }, cancellationToken: cancellationToken)))
            .ToList();

        return new Response<IReadOnlyList<ContactTypeCompanyDto>> { IsSuccess = true, Message = "Company contact types retrieved successfully.", Data = items };
    }
}
