using Dapper;
using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketCompanyDefaults.Queries;

/// <summary>
/// Handles high-performance tenant ticket default configuration detail queries.
/// </summary>
public sealed class GetTicketCompanyDefaultByIdQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetTicketCompanyDefaultByIdQuery, Response<TicketCompanyDefaultDto>>
{
    /// <summary>
    /// Retrieves a single active tenant configuration by identifier.
    /// </summary>
    public async Task<Response<TicketCompanyDefaultDto>> Handle(GetTicketCompanyDefaultByIdQuery request, CancellationToken cancellationToken)
    {
        if (!currentUserService.IsAuthenticated || currentUserService.CompanyId == Guid.Empty)
        {
            return Response<TicketCompanyDefaultDto>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
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
            WHERE tcd.Id = @Id
              AND tcd.CompanyId = @TenantId
              AND tcd.GcRecord = 0;
            """;

        var item = await connection.QuerySingleOrDefaultAsync<TicketCompanyDefaultDto>(
            new CommandDefinition(sql, new { request.Id, TenantId = currentUserService.CompanyId }, cancellationToken: cancellationToken));

        if (item is null)
        {
            return Response<TicketCompanyDefaultDto>.Error("TICKET_COMPANY_DEFAULT_NOT_FOUND", ["Ticket company default configuration not found for the current tenant."]);
        }

        return new Response<TicketCompanyDefaultDto>
        {
            IsSuccess = true,
            Message = "Ticket company default configuration retrieved successfully.",
            Data = item
        };
    }
}
