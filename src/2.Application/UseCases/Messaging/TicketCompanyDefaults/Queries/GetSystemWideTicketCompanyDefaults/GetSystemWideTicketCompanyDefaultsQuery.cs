using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketCompanyDefaults.Queries;

/// <summary>
/// Query used by SuperAdmin users to retrieve ticket default configurations across all companies with optional filters.
/// </summary>
/// <param name="PageNumber">The requested page number.</param>
/// <param name="PageSize">The requested number of items per page.</param>
/// <param name="CompanyName">Optional partial-match filter applied to the company name.</param>
/// <param name="StartCode">Optional partial-match filter applied to the start code.</param>
/// <param name="TicketStatusDefaultName">Optional partial-match filter applied to the default ticket status name.</param>
/// <param name="TicketComplexityDefaultName">Optional partial-match filter applied to the default ticket complexity name.</param>
/// <param name="TimeUnitDefaultName">Optional partial-match filter applied to the default time unit name.</param>
/// <param name="AreaName">Optional partial-match filter applied to the default area name.</param>
/// <param name="ProjectName">Optional partial-match filter applied to the default project name.</param>
/// <param name="ChannelName">Optional partial-match filter applied to the default communication channel name.</param>
public sealed record GetSystemWideTicketCompanyDefaultsQuery(
    int? PageNumber = null,
    int? PageSize = null,
    string? CompanyName = null,
    string? StartCode = null,
    string? TicketStatusDefaultName = null,
    string? TicketComplexityDefaultName = null,
    string? TimeUnitDefaultName = null,
    string? AreaName = null,
    string? ProjectName = null,
    string? ChannelName = null)
    : IRequest<Response<PagedResult<TicketCompanyDefaultDto>>>;
