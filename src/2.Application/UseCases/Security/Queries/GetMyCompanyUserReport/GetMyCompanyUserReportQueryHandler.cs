using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Security.Queries.GetSystemWideUserReport;
using MediatR;

namespace JOIN.Application.UseCases.Security.Queries.GetMyCompanyUserReport;

/// <summary>
/// Handles the user management and activity report for the authenticated user's company context.
/// </summary>
/// <param name="connectionFactory">Factory used to create engine-agnostic read connections.</param>
/// <param name="currentUserService">Current user context used to enforce tenant isolation.</param>
public class GetMyCompanyUserReportQueryHandler(
    ISqlConnectionFactory connectionFactory,
    ICurrentUserService currentUserService)
    : IRequestHandler<GetMyCompanyUserReportQuery, Response<IReadOnlyCollection<UserManagementReportDto>>>
{
    /// <summary>
    /// Retrieves the report restricted to the company resolved from the authenticated token/header context.
    /// </summary>
    public async Task<Response<IReadOnlyCollection<UserManagementReportDto>>> Handle(
        GetMyCompanyUserReportQuery request,
        CancellationToken cancellationToken)
    {
        if (!currentUserService.IsAuthenticated || currentUserService.CompanyId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("The authenticated user is not bound to a company context.");
        }

        var items = await UserManagementReportQueryHelper.ReadAsync(
            connectionFactory,
            scopedCompanyId: currentUserService.CompanyId,
            targetCompanyId: null,
            fromDate: request.FromDate,
            toDate: request.ToDate,
            roleNames: request.RoleNames,
            cancellationToken: cancellationToken);

        return new Response<IReadOnlyCollection<UserManagementReportDto>>
        {
            IsSuccess = true,
            Message = "Company user report retrieved successfully.",
            Data = items
        };
    }
}
