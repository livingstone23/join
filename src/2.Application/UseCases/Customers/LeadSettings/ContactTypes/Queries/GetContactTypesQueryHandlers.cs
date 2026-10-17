using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Customers.LeadSettings;
using JOIN.Application.Interface;
using JOIN.Application.Services.Customers;
using MediatR;

namespace JOIN.Application.UseCases.Customers.LeadSettings.ContactTypes.Queries;

internal static class ContactTypeQuerySql
{
    internal const string Select = """
        SELECT
            t.Id,
            t.GcRecord,
            t.Code,
            t.Name,
            t.Icon,
            t.CommunicationChannelCode,
            t.SchedulesCalendarEvent,
            t.AsksDuration,
            t.SortOrder,
            t.IsActive,
            t.Created AS CreatedAt
        FROM customer.contacttypes t
        """;

    internal static Response<T>? Guard<T>(ICurrentUserService currentUserService) =>
        currentUserService.IsInRole("SuperAdmin")
            ? null
            : Response<T>.Error(LeadSettingsErrorCodes.SuperAdminRequired, ["Only a SuperAdmin can read the master contact types."]);
}

/// <summary>Handles <see cref="GetContactTypesQuery"/>.</summary>
public sealed class GetContactTypesQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService)
    : IRequestHandler<GetContactTypesQuery, Response<IReadOnlyList<ContactTypeDto>>>
{
    public async Task<Response<IReadOnlyList<ContactTypeDto>>> Handle(GetContactTypesQuery request, CancellationToken cancellationToken)
    {
        if (ContactTypeQuerySql.Guard<IReadOnlyList<ContactTypeDto>>(currentUserService) is { } denied)
        {
            return denied;
        }

        var deleted = request.IncludeDeleted == true ? string.Empty : "AND t.GcRecord = 0";
        var inactive = request.IncludeInactive == false ? "AND t.IsActive = TRUE" : string.Empty;
        var sql = $"""
            {ContactTypeQuerySql.Select}
            WHERE 1 = 1
              {deleted}
              {inactive}
            ORDER BY t.SortOrder, t.Name;
            """;

        using var connection = connectionFactory.CreateConnection();
        var items = (await connection.QueryAsync<ContactTypeDto>(new CommandDefinition(sql, cancellationToken: cancellationToken))).ToList();
        return new Response<IReadOnlyList<ContactTypeDto>> { IsSuccess = true, Message = "Contact types retrieved successfully.", Data = items };
    }
}

/// <summary>Handles <see cref="GetContactTypeByIdQuery"/>.</summary>
public sealed class GetContactTypeByIdQueryHandler(ISqlConnectionFactory connectionFactory, ICurrentUserService currentUserService)
    : IRequestHandler<GetContactTypeByIdQuery, Response<ContactTypeDto>>
{
    public async Task<Response<ContactTypeDto>> Handle(GetContactTypeByIdQuery request, CancellationToken cancellationToken)
    {
        if (ContactTypeQuerySql.Guard<ContactTypeDto>(currentUserService) is { } denied)
        {
            return denied;
        }

        var deleted = request.IncludeDeleted == true ? string.Empty : "AND t.GcRecord = 0";
        var sql = $"""
            {ContactTypeQuerySql.Select}
            WHERE t.Id = @Id
              {deleted};
            """;

        using var connection = connectionFactory.CreateConnection();
        var item = await connection.QuerySingleOrDefaultAsync<ContactTypeDto>(new CommandDefinition(sql, new { request.Id }, cancellationToken: cancellationToken));
        return item is null
            ? Response<ContactTypeDto>.Error(LeadSettingsErrorCodes.ContactTypeNotFound, ["The contact type was not found."])
            : new Response<ContactTypeDto> { IsSuccess = true, Message = "The contact type was retrieved successfully.", Data = item };
    }
}
