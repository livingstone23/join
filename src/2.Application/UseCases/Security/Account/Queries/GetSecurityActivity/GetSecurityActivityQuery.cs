using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Account;
using MediatR;

namespace JOIN.Application.UseCases.Security.Account.Queries.GetSecurityActivity;

/// <summary>
/// Backs <c>GET /api/v1/account/security-activity</c> (SPEC 26 / F9). Returns the caller's
/// paginated security-event feed.
/// </summary>
/// <param name="PageNumber">1-based page number; clamped to &gt;= 1.</param>
/// <param name="PageSize">Page size; clamped to 1..100.</param>
public sealed record GetSecurityActivityQuery(int PageNumber, int PageSize)
    : IRequest<Response<PagedResult<SecurityActivityEventDto>>>;
