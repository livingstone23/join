using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketComplexities.Queries;

/// <summary>
/// Query to retrieve a paginated system-wide list of ticket complexities with optional filters.
/// </summary>
/// <param name="PageNumber">The requested page number.</param>
/// <param name="PageSize">The requested number of items per page.</param>
/// <param name="Name">Optional search term to filter by ticket complexity name.</param>
/// <param name="IsActive">Optional exact-match filter for the active flag.</param>
/// <param name="CompanyName">Optional partial-match filter applied to the company name.</param>
public sealed record GetSystemWideTicketComplexitiesQuery(
    int? PageNumber = null,
    int? PageSize = null,
    string? Name = null,
    bool? IsActive = null,
    string? CompanyName = null)
    : IRequest<Response<PagedResult<TicketComplexityDto>>>;
