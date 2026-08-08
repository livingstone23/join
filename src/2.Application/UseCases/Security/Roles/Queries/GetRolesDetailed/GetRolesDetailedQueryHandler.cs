using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.Interface.Persistence.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Roles.Queries.GetRolesDetailed;

/// <summary>
/// Handler for the paged roles listing. Sanitizes page/pageSize and delegates to <see cref="IRoleRepository.GetPagedAsync"/>.
/// </summary>
public sealed class GetRolesDetailedQueryHandler(IRoleRepository roleRepository)
    : IRequestHandler<GetRolesDetailedQuery, Response<PagedResult<RoleDto>>>
{
    private const int MaxPageSize = 100;
    private const int MinPageSize = 1;
    private const int DefaultPageSize = 20;

    private readonly IRoleRepository _roleRepository = roleRepository ?? throw new ArgumentNullException(nameof(roleRepository));

    public async Task<Response<PagedResult<RoleDto>>> Handle(GetRolesDetailedQuery request, CancellationToken cancellationToken)
    {
        var sanitizedPage = request.Page < 1 ? 1 : request.Page;
        var requestedPageSize = request.PageSize < MinPageSize ? DefaultPageSize : request.PageSize;
        var sanitizedPageSize = Math.Min(requestedPageSize, MaxPageSize);

        var (items, total) = await _roleRepository.GetPagedAsync(
            request.Name,
            request.IsActive,
            sanitizedPage,
            sanitizedPageSize,
            cancellationToken);

        var pagedResult = new PagedResult<RoleDto>
        {
            Items = items,
            PageNumber = sanitizedPage,
            PageSize = sanitizedPageSize,
            TotalCount = total,
            TotalPages = total == 0 ? 0 : (int)Math.Ceiling(total / (double)sanitizedPageSize)
        };

        return new Response<PagedResult<RoleDto>>
        {
            IsSuccess = true,
            Message = "Roles retrieved successfully.",
            Data = pagedResult
        };
    }
}
