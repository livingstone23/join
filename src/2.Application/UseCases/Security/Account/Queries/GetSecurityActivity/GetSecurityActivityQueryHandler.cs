using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Account;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using JOIN.Domain.Security;
using MediatR;

namespace JOIN.Application.UseCases.Security.Account.Queries.GetSecurityActivity;

/// <summary>
/// Returns the caller's paginated security-activity feed. Clamps <c>pageNumber</c>
/// to &gt;= 1 and <c>pageSize</c> to 1..100, projects rows to DTOs (enum names — never
/// the int values), and returns a <see cref="PagedResult{T}"/>.
/// </summary>
/// <param name="currentUserService">Resolves the calling user id.</param>
/// <param name="securityEventRepository">Dapper-backed audit feed queries.</param>
public sealed class GetSecurityActivityQueryHandler(
    ICurrentUserService currentUserService,
    ISecurityEventRepository securityEventRepository)
    : IRequestHandler<GetSecurityActivityQuery, Response<PagedResult<SecurityActivityEventDto>>>
{
    private const int MinPageNumber = 1;
    private const int MinPageSize = 1;
    private const int MaxPageSize = 100;

    private readonly ICurrentUserService _currentUserService = currentUserService;
    private readonly ISecurityEventRepository _securityEventRepository = securityEventRepository;

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

        var pageNumber = Math.Max(MinPageNumber, request.PageNumber);
        var pageSize = Math.Clamp(request.PageSize, MinPageSize, MaxPageSize);

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
