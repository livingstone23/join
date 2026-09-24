using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Account;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Security;
using MediatR;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Security.Account.Queries.GetSecurityActivity;

/// <summary>
/// Returns the caller's paginated security-activity feed. Sanitizes <c>pageNumber</c>/<c>pageSize</c>
/// via the shared <see cref="PaginationSettings"/>, projects rows to DTOs (enum names — never
/// the int values), and returns a <see cref="PagedResult{T}"/>.
/// </summary>
/// <param name="currentUserService">Resolves the calling user id.</param>
/// <param name="securityEventRepository">Dapper-backed audit feed queries.</param>
/// <param name="paginationOptions">Configurable pagination defaults shared across paged endpoints.</param>
public sealed class GetSecurityActivityQueryHandler(
    ICurrentUserService currentUserService,
    ISecurityEventRepository securityEventRepository,
    IOptions<PaginationSettings> paginationOptions)
    : IRequestHandler<GetSecurityActivityQuery, Response<PagedResult<SecurityActivityEventDto>>>
{
    private readonly ICurrentUserService _currentUserService = currentUserService;
    private readonly ISecurityEventRepository _securityEventRepository = securityEventRepository;
    private readonly PaginationSettings _paginationSettings = paginationOptions.Value ?? new();

    public async Task<Response<PagedResult<SecurityActivityEventDto>>> Handle(
        GetSecurityActivityQuery request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(_currentUserService.UserId, out var callerUserId) || callerUserId == Guid.Empty)
        {
            return Response<PagedResult<SecurityActivityEventDto>>.Error(
                "USER_NOT_FOUND",
                ["The authenticated user is not available."]);
        }

        var (pageNumber, pageSize) = _paginationSettings.Sanitize(request.PageNumber, request.PageSize);

        var rows = await _securityEventRepository
            .ListByUserPagedAsync(callerUserId, pageNumber, pageSize, cancellationToken);

        var total = await _securityEventRepository.CountByUserAsync(callerUserId, cancellationToken);

        var items = rows.Select(row => new SecurityActivityEventDto
        {
            OccurredAtUtc = row.OccurredAtUtc,
            Event = ((SecurityEventType)row.EventType).ToString(),
            Result = ((SecurityEventResult)row.Result).ToString(),
            IpAddress = row.IpAddress,
            Device = row.UserAgent
        }).ToList();

        var pageCount = pageSize == 0 ? 0 : (int)Math.Ceiling(total / (double)pageSize);
        var result = new PagedResult<SecurityActivityEventDto>
        {
            Items = items,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = total,
            TotalPages = pageCount
        };

        return new Response<PagedResult<SecurityActivityEventDto>>
        {
            IsSuccess = true,
            Message = "OK",
            Data = result
        };
    }
}
