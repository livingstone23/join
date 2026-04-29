using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using MediatR;



namespace JOIN.Application.UseCases.Security.SystemOptions.Queries;



/// <summary>
/// Query para obtener paginación de SystemOptions globales.
/// </summary>
public sealed record GetSystemOptionsPagedQuery(
    int? PageNumber = null,
    int? PageSize = null,
    string? Name = null
) : IRequest<Response<PagedResult<SystemOptionListItemDto>>>;
