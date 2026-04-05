using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Queries.GetSystemWideUserReport;

/// <summary>
/// Requests the system-wide user management and activity report.
/// </summary>
/// <param name="FromDate">Optional inclusive start date for the activity window.</param>
/// <param name="ToDate">Optional inclusive end date for the activity window.</param>
/// <param name="TargetCompanyId">Optional company filter for the report.</param>
/// <param name="RoleNames">Optional inclusive role-name filter scoped through <c>UserRoleCompanies</c>.</param>
public record GetSystemWideUserReportQuery(
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    Guid? TargetCompanyId = null,
    string[]? RoleNames = null)
    : IRequest<Response<IReadOnlyCollection<UserManagementReportDto>>>;
