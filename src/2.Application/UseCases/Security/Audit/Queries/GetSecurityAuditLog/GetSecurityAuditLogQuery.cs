using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Audit;
using MediatR;

namespace JOIN.Application.UseCases.Security.Audit.Queries.GetSecurityAuditLog;

/// <summary>
/// Paginated read of <c>Security.AuditLogs</c>. Tenant-scoped by the JWT's
/// <c>CompanyId</c> unless the caller is SuperAdmin and supplies <c>AllTenants = true</c>.
/// </summary>
public sealed record GetSecurityAuditLogQuery(
    string? Entity = null,
    Guid? EntityId = null,
    string? ChangedBy = null,
    string? Action = null,
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    int PageNumber = 1,
    int PageSize = 20,
    bool AllTenants = false)
    : IRequest<Response<PagedResult<SecurityAuditLogItemDto>>>;
