using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.IdentificationTypes.Queries;

/// <summary>
/// Query used to retrieve a paginated list of identification document types.
/// </summary>
/// <param name="PageNumber">Optional page number requested by the client.</param>
/// <param name="PageSize">Optional page size requested by the client.</param>
/// <param name="Name">Optional partial-match filter applied to the identification type name.</param>
/// <param name="Created">Optional exact day filter applied to the creation timestamp.</param>
/// <param name="CreatedFrom">Optional inclusive lower bound used to filter by creation date.</param>
/// <param name="CreatedTo">Optional inclusive upper bound used to filter by creation date.</param>
public sealed record GetIdentificationTypesQuery(
    int? PageNumber = null,
    int? PageSize = null,
    string? Name = null,
    DateTime? Created = null,
    DateTime? CreatedFrom = null,
    DateTime? CreatedTo = null)
    : IRequest<Response<PagedResult<IdentificationTypeListItemDto>>>;