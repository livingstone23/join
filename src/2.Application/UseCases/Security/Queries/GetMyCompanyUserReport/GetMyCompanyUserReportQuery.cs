using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Queries.GetMyCompanyUserReport;

/// <summary>
/// Paginated user-management report restricted to the caller's tenant. SPEC 28 /
/// F7, item 21. The shape of the response changed from
/// <c>Response&lt;IReadOnlyCollection&lt;UserManagementReportDto&gt;&gt;</c> to
/// <c>Response&lt;PagedResult&lt;UserManagementReportDto&gt;&gt;</c> — that is the
/// breaking change this spec introduces. The report still runs through Dapper so
/// <c>IsActive</c> and <c>Search</c> filters work end-to-end (the EF global filter
/// would otherwise hide inactive users).
/// </summary>
/// <param name="FromDate">Optional inclusive start date for the reporting window.</param>
/// <param name="ToDate">Optional inclusive end date for the reporting window.</param>
/// <param name="RoleNames">Optional role-name filter applied to the report.</param>
/// <param name="PageNumber">1-based page index; values below 1 are clamped to 1.</param>
/// <param name="PageSize">Items per page; values outside [1, 50] are clamped (default 10).</param>
/// <param name="Search">Optional partial match against Email or full name, case-insensitive.</param>
/// <param name="IsActive">Optional active/inactive filter; null returns both.</param>
public record GetMyCompanyUserReportQuery(
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    string[]? RoleNames = null,
    int PageNumber = 1,
    int PageSize = 10,
    string? Search = null,
    bool? IsActive = null)
    : IRequest<Response<PagedResult<UserManagementReportDto>>>;