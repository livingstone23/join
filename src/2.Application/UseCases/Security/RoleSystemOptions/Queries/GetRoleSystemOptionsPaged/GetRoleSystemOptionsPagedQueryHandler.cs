using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Application.Mappings.Security.RoleSystemOption;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Security.RoleSystemOptions.Queries;

/// <summary>
/// Handles tenant-scoped paged retrieval of RoleSystemOption rules.
/// </summary>
public sealed class GetRoleSystemOptionsPagedQueryHandler(
    IRoleSystemOptionsRepository repository,
    ICurrentUserService currentUserService,
    IRoleSystemOptionMapper mapper,
    IOptions<PaginationSettings> paginationOptions)
    : IRequestHandler<GetRoleSystemOptionsPagedQuery, Response<PagedResult<RoleSystemOptionListItemDto>>>
{
    private readonly PaginationSettings _paginationSettings = paginationOptions.Value ?? new();

    public async Task<Response<PagedResult<RoleSystemOptionListItemDto>>> Handle(GetRoleSystemOptionsPagedQuery request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<PagedResult<RoleSystemOptionListItemDto>>.Error("INVALID_COMPANY_ID", ["A valid company context is required."]);
        }

        var defaultPageNumber = _paginationSettings.DefaultPageNumber < 1 ? 1 : _paginationSettings.DefaultPageNumber;
        var defaultPageSize = _paginationSettings.DefaultPageSize < 1 ? 10 : _paginationSettings.DefaultPageSize;
        var maxPageSize = _paginationSettings.MaxPageSize < defaultPageSize ? defaultPageSize : _paginationSettings.MaxPageSize;

        var sanitizedPageNumber = request.PageNumber.GetValueOrDefault(defaultPageNumber);
        sanitizedPageNumber = sanitizedPageNumber < 1 ? defaultPageNumber : sanitizedPageNumber;

        var requestedPageSize = request.PageSize.GetValueOrDefault(defaultPageSize);
        var sanitizedPageSize = requestedPageSize < 1 ? defaultPageSize : Math.Min(requestedPageSize, maxPageSize);
        var offset = (sanitizedPageNumber - 1) * sanitizedPageSize;

        var filter = new RoleSystemOptionQueryFilter(
            companyId,
            request.RoleId,
            request.SystemOptionId,
            request.RoleName,
            request.SystemOptionName,
            request.CompanyName,
            request.CanRead,
            request.CanCreate,
            request.CanUpdate,
            request.CanDelete,
            offset,
            sanitizedPageSize);

        var (items, totalCount) = await repository.GetPagedWithNamesAsync(filter, false, cancellationToken);
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
