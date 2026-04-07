using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Queries.GetSystemWideUserReport;

/// <summary>
/// Represents the query used to obtain the system-wide user-management and activity report.
/// This request is intended for elevated administrative scenarios where cross-company visibility is permitted and required.
/// </summary>
/// <param name="FromDate">Optional inclusive start date for the reporting window.</param>
/// <param name="ToDate">Optional inclusive end date for the reporting window.</param>
/// <param name="TargetCompanyId">Optional company identifier used to narrow the report to a single tenant while still executing through the system-wide report pipeline.</param>
/// <param name="RoleNames">Optional role-name filter applied to the resulting report rows.</param>
public record GetSystemWideUserReportQuery(
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    Guid? TargetCompanyId = null,
    string[]? RoleNames = null)
    : IRequest<Response<IReadOnlyCollection<UserManagementReportDto>>>;
