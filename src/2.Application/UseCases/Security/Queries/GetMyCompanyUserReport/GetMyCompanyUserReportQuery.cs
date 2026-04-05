using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Queries.GetMyCompanyUserReport;

/// <summary>
/// Requests the user management and activity report restricted to the caller's company.
/// </summary>
/// <param name="FromDate">Optional inclusive start date for the activity window.</param>
/// <param name="ToDate">Optional inclusive end date for the activity window.</param>
/// <param name="RoleNames">Optional inclusive role-name filter scoped through <c>UserRoleCompanies</c>.</param>
public record GetMyCompanyUserReportQuery(
    DateTime? FromDate = null,
    DateTime? ToDate = null,
    string[]? RoleNames = null)
    : IRequest<Response<IReadOnlyCollection<UserManagementReportDto>>>;
