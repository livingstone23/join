using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TicketUserCompanies.Queries.GetTicketUserCompanies;

/// <summary>
/// Paged, tenant-scoped query over the ticket-roster, with optional capability filters.
/// </summary>
public record GetTicketUserCompaniesQuery : IRequest<Response<PagedResult<TicketUserCompanyDto>>>
{
    /// <summary>
    /// Gets or sets the 1-based page number. Invalid values fall back to the default via <c>PaginationSettings</c>.
    /// </summary>
    public int? PageNumber { get; init; }

    /// <summary>
    /// Gets or sets the page size. Invalid values fall back to the default via <c>PaginationSettings</c>.
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
    /// Gets whether deleted rows are included. Honored only for <c>SuperAdmin</c> (SPEC 41, <see cref="SoftDeleteVisibility"/>).
    /// </summary>
    public bool? IncludeDeleted { get; init; }

    /// <summary>
    /// Gets an optional explicit company, honored only for <c>SuperAdmin</c> (<see cref="TenantResolver"/>).
    /// </summary>
    public Guid? CompanyId { get; init; }
}
