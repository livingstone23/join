using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Genders.Queries;

/// <summary>
/// Query used to retrieve a paginated and filterable gender list for the authenticated tenant.
/// </summary>
/// <param name="PageNumber">The optional page number to retrieve. When omitted, the configured default value is used.</param>
/// <param name="PageSize">The optional page size to retrieve. When omitted, the configured default value is used.</param>
/// <param name="Code">Optional inclusive partial-match filter applied to the gender code.</param>
/// <param name="Name">Optional inclusive partial-match filter applied to the gender name.</param>
/// <param name="IsActive">Optional exact-match filter applied to the active state.</param>
public sealed record GetGendersQuery(
    int? PageNumber = null,
    int? PageSize = null,
    string? Code = null,
    string? Name = null,
    bool? IsActive = null)
    : IRequest<Response<PagedResult<GenderDto>>>;
