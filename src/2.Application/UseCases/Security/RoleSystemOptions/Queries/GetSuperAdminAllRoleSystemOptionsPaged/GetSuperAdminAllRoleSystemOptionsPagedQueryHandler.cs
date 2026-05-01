using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.Mappings.Security.RoleSystemOption;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Security.RoleSystemOptions.Queries;

/// <summary>
/// Handles SuperAdmin paged retrieval of RoleSystemOption rules across companies.
/// </summary>
public sealed class GetSuperAdminAllRoleSystemOptionsPagedQueryHandler(
    IRoleSystemOptionsRepository repository,
    IRoleSystemOptionMapper mapper,
    IOptions<PaginationSettings> paginationOptions)
    : IRequestHandler<GetSuperAdminAllRoleSystemOptionsPagedQuery, Response<PagedResult<RoleSystemOptionListItemDto>>>
{
    private readonly PaginationSettings _paginationSettings = paginationOptions.Value ?? new();

    public async Task<Response<PagedResult<RoleSystemOptionListItemDto>>> Handle(GetSuperAdminAllRoleSystemOptionsPagedQuery request, CancellationToken cancellationToken)
    {
        var defaultPageNumber = _paginationSettings.DefaultPageNumber < 1 ? 1 : _paginationSettings.DefaultPageNumber;
        var defaultPageSize = _paginationSettings.DefaultPageSize < 1 ? 10 : _paginationSettings.DefaultPageSize;
        var maxPageSize = _paginationSettings.MaxPageSize < defaultPageSize ? defaultPageSize : _paginationSettings.MaxPageSize;

        var sanitizedPageNumber = request.PageNumber.GetValueOrDefault(defaultPageNumber);
        sanitizedPageNumber = sanitizedPageNumber < 1 ? defaultPageNumber : sanitizedPageNumber;

        var requestedPageSize = request.PageSize.GetValueOrDefault(defaultPageSize);
        var sanitizedPageSize = requestedPageSize < 1 ? defaultPageSize : Math.Min(requestedPageSize, maxPageSize);
        var offset = (sanitizedPageNumber - 1) * sanitizedPageSize;

        var filter = new RoleSystemOptionQueryFilter(
            CompanyId: request.CompanyId,
            RoleId: request.RoleId,
            SystemOptionId: request.SystemOptionId,
            RoleName: request.RoleName,
            SystemOptionName: request.SystemOptionName,
            CompanyName: request.CompanyName,
            CanRead: request.CanRead,
            CanCreate: request.CanCreate,
            CanUpdate: request.CanUpdate,
            CanDelete: request.CanDelete,
            Offset: offset,
            PageSize: sanitizedPageSize);

        var (items, totalCount) = await repository.GetPagedWithNamesAsync(filter, true, cancellationToken);
        var data = new PagedResult<RoleSystemOptionListItemDto>
        {
            Items = items.Select(mapper.ToListItemDto).ToList(),
            PageNumber = sanitizedPageNumber,
            PageSize = sanitizedPageSize,
            TotalCount = totalCount,
            TotalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)sanitizedPageSize)
        };

        return new Response<PagedResult<RoleSystemOptionListItemDto>>
        {
            IsSuccess = true,
            Message = "Role system options retrieved successfully.",
            Data = data
        };
    }
}
