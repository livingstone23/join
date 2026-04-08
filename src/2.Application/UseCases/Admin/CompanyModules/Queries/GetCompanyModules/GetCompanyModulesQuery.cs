using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.CompanyModules.Queries;

/// <summary>
/// Query used to retrieve a paginated list of company module assignments within a tenant scope.
/// </summary>
/// <param name="CompanyId">The tenant identifier used to scope the result.</param>
/// <param name="PageNumber">Optional page number requested by the client.</param>
/// <param name="PageSize">Optional page size requested by the client.</param>
/// <param name="CompanyName">Optional partial-match filter applied to the company name.</param>
/// <param name="ModuleName">Optional partial-match filter applied to the system module name.</param>
/// <param name="CreatedFrom">Optional inclusive lower bound used to filter by creation date.</param>
/// <param name="CreatedTo">Optional inclusive upper bound used to filter by creation date.</param>
public sealed record GetCompanyModulesQuery(
    Guid CompanyId,
    int? PageNumber = null,
    int? PageSize = null,
    string? CompanyName = null,
    string? ModuleName = null,
    DateTime? CreatedFrom = null,
    DateTime? CreatedTo = null)
    : IRequest<Response<PagedResult<CompanyModuleListItemDto>>>;
