using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using MediatR;

namespace JOIN.Application.UseCases.Security.Queries.GetSystemWideUserReport;

/// <summary>
/// Handles system-wide user management and activity report queries. Unchanged by
/// SPEC 28 / F7 — the endpoint continues to call <see cref="UserManagementReportQueryHelper.ReadAsync"/>
/// (unpaginated) and keeps its <c>IReadOnlyCollection&lt;T&gt;</c> response shape.
/// </summary>
/// <param name="connectionFactory">Factory used to create engine-agnostic read connections.</param>
public sealed class GetSystemWideUserReportQueryHandler(ISqlConnectionFactory connectionFactory)
    : IRequestHandler<GetSystemWideUserReportQuery, Response<IReadOnlyCollection<UserManagementReportDto>>>
{
    /// <summary>
    /// Retrieves the global report across all companies, optionally filtered by company, date range, and roles.
    /// </summary>
    public async Task<Response<IReadOnlyCollection<UserManagementReportDto>>> Handle(
        GetSystemWideUserReportQuery request,
        CancellationToken cancellationToken)
    {
        var items = await UserManagementReportQueryHelper.ReadAsync(
            connectionFactory,
            scopedCompanyId: null,
            targetCompanyId: request.TargetCompanyId,
            fromDate: request.FromDate,
            toDate: request.ToDate,
            roleNames: request.RoleNames,
            cancellationToken: cancellationToken);

        return new Response<IReadOnlyCollection<UserManagementReportDto>>
        {
            IsSuccess = true,
            Message = "System-wide user report retrieved successfully.",
            Data = items
        };
    }
}