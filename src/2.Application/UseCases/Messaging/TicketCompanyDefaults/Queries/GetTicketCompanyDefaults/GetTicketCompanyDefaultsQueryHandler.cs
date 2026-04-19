using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketCompanyDefaults.Queries;

/// <summary>
/// Handles high-performance tenant ticket default configuration list queries.
/// </summary>
public sealed class GetTicketCompanyDefaultsQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetTicketCompanyDefaultsQuery, Response<IReadOnlyCollection<TicketCompanyDefaultDto>>>
{
    /// <summary>
    /// Retrieves the active configuration list for the current tenant.
    /// </summary>
    public async Task<Response<IReadOnlyCollection<TicketCompanyDefaultDto>>> Handle(GetTicketCompanyDefaultsQuery request, CancellationToken cancellationToken)
    {
        if (!currentUserService.IsAuthenticated || currentUserService.CompanyId == Guid.Empty)
        {
            return Response<IReadOnlyCollection<TicketCompanyDefaultDto>>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        using var connection = connectionFactory.CreateConnection();

        const string sql = """
            SELECT
                tcd.Id,
                tcd.CompanyId,
                tcd.StartCode,
                tcd.CodeSequenceLength,
                tcd.UsePersonalizedCode,
                tcd.TicketStatusDefaultId,
                ts.Name AS StatusName,
                tcd.TicketComplexityDefaultId,
                tc.Name AS ComplexityName,
                tcd.TimeUnitDefaultId,
                tu.Name AS TimeUnitName,
                tcd.AreaDefaultId,
                a.Name AS AreaName,
                tcd.ProjectDefaultId,
                p.Name AS ProjectName,
                tcd.ChannelDefaultId,
                ch.Name AS ChannelName,
                tcd.Created AS CreatedAt
            FROM Messaging.TicketCompanyDefaults tcd
            LEFT JOIN Messaging.TicketStatuses ts ON tcd.TicketStatusDefaultId = ts.Id
            LEFT JOIN Messaging.TicketComplexities tc ON tcd.TicketComplexityDefaultId = tc.Id
            LEFT JOIN Messaging.TimeUnits tu ON tcd.TimeUnitDefaultId = tu.Id
            LEFT JOIN Admin.Areas a ON tcd.AreaDefaultId = a.Id
            LEFT JOIN Admin.Projects p ON tcd.ProjectDefaultId = p.Id
            LEFT JOIN Common.CommunicationChannels ch ON tcd.ChannelDefaultId = ch.Id
            WHERE tcd.CompanyId = @TenantId
              AND tcd.GcRecord = 0
            ORDER BY tcd.Created DESC;
            """;

        var items = (await connection.QueryAsync<TicketCompanyDefaultDto>(
            new CommandDefinition(sql, new { TenantId = currentUserService.CompanyId }, cancellationToken: cancellationToken))).AsList();

        return new Response<IReadOnlyCollection<TicketCompanyDefaultDto>>
        {
            IsSuccess = true,
            Message = "Ticket company default configurations retrieved successfully.",
            Data = items
        };
    }
}
