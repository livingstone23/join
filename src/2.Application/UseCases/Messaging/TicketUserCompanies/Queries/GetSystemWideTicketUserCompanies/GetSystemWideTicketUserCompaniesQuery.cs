using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketUserCompanies.Queries.GetSystemWideTicketUserCompanies;

/// <summary>
/// Cross-tenant paged query over the ticket-roster, restricted to platform-level <c>SuperAdmin</c>
/// at the controller (the application layer does not re-check the role).
/// </summary>
public record GetSystemWideTicketUserCompaniesQuery : IRequest<Response<PagedResult<TicketUserCompanyDto>>>
{
    /// <summary>
    /// Gets or sets the 1-based page number.
    /// </summary>
    public int? PageNumber { get; init; }

    /// <summary>
    /// Gets or sets the page size.
    /// </summary>
    public int? PageSize { get; init; }

    /// <summary>
    /// Gets or sets an optional filter that returns only the rows linked to this user.
    /// </summary>
    public Guid? UserId { get; init; }

    /// <summary>
    /// Gets or sets an optional filter on the IsSuperAdminTicket flag.
    /// </summary>
    public bool? IsSuperAdminTicket { get; init; }

    /// <summary>
    /// Gets or sets an optional filter on the CanFinishTicket flag.
    /// </summary>
    public bool? CanFinishTicket { get; init; }

    /// <summary>
    /// Gets or sets an optional filter on the CanResolveTicket flag.
    /// </summary>
    public bool? CanResolveTicket { get; init; }

    /// <summary>
    /// Gets or sets an optional filter on the company name (LIKE match).
    /// </summary>
    public string? CompanyName { get; init; }
}
