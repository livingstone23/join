using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Queries.GetMyCompanyUserReport;

/// <summary>
/// Represents the query used to obtain the user-management and activity report restricted to the authenticated caller's effective company context.
/// This request is intended for tenant-scoped administrative reporting where data must remain isolated to the current company.
/// </summary>
/// <param name="FromDate">Optional inclusive start date for the reporting window.</param>
/// <param name="ToDate">Optional inclusive end date for the reporting window.</param>
/// <param name="RoleNames">Optional role-name filter applied to the report through tenant-scoped user-role assignments.</param>
public record GetMyCompanyUserReportQuery(
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    string[]? RoleNames = null)
    : IRequest<Response<IReadOnlyCollection<UserManagementReportDto>>>;
