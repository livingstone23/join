using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence.Security;
using MediatR;
using Microsoft.Extensions.Logging;

namespace JOIN.Application.UseCases.Security.Users.Commands.ChangeUserStatus;

/// <summary>
/// SPEC 27 item 16 — flips a user's <c>IsActive</c> flag via Dapper (because the EF
/// global filter hides inactive users from <c>UserManager</c>). On deactivation, also
/// revokes active refresh tokens, closes connection logs and invalidates the permission
/// cache. Cache invalidation failures are swallowed: a stale cache TTL will heal itself.
/// </summary>
public sealed class ChangeUserStatusCommandHandler(
    IUserAdminRepository userAdminRepository,
    ICurrentUserService currentUserService,
    IPermissionService permissionService,
    ILogger<ChangeUserStatusCommandHandler> logger)
    : IRequestHandler<ChangeUserStatusCommand, Response<bool>>
{
    private readonly IUserAdminRepository _userAdminRepository = userAdminRepository;
    private readonly ICurrentUserService _currentUserService = currentUserService;
    private readonly IPermissionService _permissionService = permissionService;
    private readonly ILogger<ChangeUserStatusCommandHandler> _logger = logger;

    public async Task<Response<bool>> Handle(ChangeUserStatusCommand request, CancellationToken cancellationToken)
    {
        var companyId = _currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<bool>.Error("TENANT_REQUIRED", ["A tenant context is required to change user status."]);
        }

        var callerUserId = Guid.TryParse(_currentUserService.UserId, out var parsed) ? parsed : Guid.Empty;
        if (callerUserId != Guid.Empty && callerUserId == request.UserId)
        {
            return Response<bool>.Error("CANNOT_CHANGE_OWN_STATUS", ["An administrator cannot change their own status."]);
        }

        var snapshot = await _userAdminRepository.GetAdminSnapshotAsync(request.UserId, companyId, cancellationToken);
        if (snapshot is null || !snapshot.HasMembership)
        {
            return Response<bool>.Error("USER_NOT_FOUND", ["The user was not found in this tenant."]);
        }

        if (snapshot.IsActive == request.IsActive)
        {
            return Response<bool>.Error("STATUS_UNCHANGED", ["The user already has the requested status."]);
        }

        var utcNow = DateTime.UtcNow;
        var modifiedBy = _currentUserService.UserId;

        var updated = await _userAdminRepository.SetUserActiveStatusAsync(
            request.UserId, request.IsActive, request.Reason, modifiedBy, utcNow, cancellationToken);
        if (!updated)
        {
            return Response<bool>.Error("USER_NOT_FOUND", ["The user was not found or could not be updated."]);
        }

        if (!request.IsActive)
        {
            // Deactivation side-effects — best-effort, not transactional.
            await _userAdminRepository.RevokeActiveRefreshTokensAsync(request.UserId, utcNow, cancellationToken);
            await _userAdminRepository.CloseActiveConnectionsAsync(request.UserId, utcNow, cancellationToken);
        }

        try
        {
            await _permissionService.InvalidateUserCacheAsync(companyId, request.UserId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to invalidate permission cache for user {UserId} after status change.", request.UserId);
        }

        return new Response<bool>
        {
            IsSuccess = true,
            Message = request.IsActive ? "User activated." : "User deactivated.",
            Data = true
        };
    }
}
