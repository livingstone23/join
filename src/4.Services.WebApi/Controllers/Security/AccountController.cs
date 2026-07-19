using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Account;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Security.Account.Commands.ChangeMyPassword;
using JOIN.Application.UseCases.Security.Account.Commands.RequestEmailChange;
using JOIN.Application.UseCases.Security.Account.Commands.UpdateMyProfile;
using JOIN.Application.UseCases.Security.Account.Queries.GetMyProfile;
using JOIN.Application.UseCases.Security.Account.Queries.GetMySessions;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;



namespace JOIN.Services.WebApi.Controllers.Security;



/// <summary>
/// Exposes authenticated self-management endpoints for the current user account.
/// The controller remains thin and delegates all business logic to the Application layer via MediatR.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/account")]
[Produces("application/json")]
[Authorize]
[PermissionResource("Users")]
public class AccountController(ISender sender, ICurrentUserService currentUserService) : ControllerBase
{
    private readonly ISender _sender = sender ?? throw new ArgumentNullException(nameof(sender));
    private readonly ICurrentUserService _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));

    /// <summary>
    /// Returns the authenticated user's basic profile information.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the request while the query is being processed.</param>
    /// <returns>
    /// Returns <c>200</c> with profile data, <c>400</c> for invalid request state,
    /// <c>401</c> when the caller is not authenticated, or <c>404</c> when the profile cannot be found.
    /// </returns>
    [HttpGet("profile")]
    [ProducesResponseType(typeof(Response<AccountProfileResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(Response<object>.Error("AUTHENTICATED_USER_REQUIRED"));
        }

        var response = await _sender.Send(new GetMyProfileQuery(userId), cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Updates the authenticated user's basic profile data and communication channels.
    /// Email and password are intentionally excluded from this operation.
    /// </summary>
    /// <param name="request">The payload containing profile fields and communication channels to update.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the command is being processed.</param>
    /// <returns>
    /// Returns <c>200</c> when the update request is accepted, <c>400</c> for invalid payload,
    /// <c>401</c> when the caller is not authenticated, or <c>404</c> when the profile cannot be found.
    /// </returns>
    [HttpPut("profile")]
    [ProducesResponseType(typeof(Response<AccountProfileResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateAccountProfileRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(Response<object>.Error("AUTHENTICATED_USER_REQUIRED"));
        }

        var response = await _sender.Send(new UpdateMyProfileCommand
        {
            UserId = userId,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PhoneNumber = request.PhoneNumber
        }, cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Changes the authenticated user's password by validating the current password and applying a new one.
    /// </summary>
    /// <param name="request">The payload containing current and new password values.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the command is being processed.</param>
    /// <returns>
    /// Returns <c>200</c> when the change request is accepted, <c>400</c> for invalid payload,
    /// <c>401</c> when the caller is not authenticated, or <c>404</c> when the account cannot be found.
    /// </returns>
    [HttpPost("change-password")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(Response<object>.Error("AUTHENTICATED_USER_REQUIRED"));
        }

        var response = await _sender.Send(new ChangeMyPasswordCommand
        {
            UserId = userId,
            OldPassword = request.OldPassword,
            NewPassword = request.NewPassword,
            ConfirmPassword = request.ConfirmPassword
        }, cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Requests an email change confirmation flow for the authenticated user.
    /// </summary>
    /// <param name="request">The payload containing the new email address to confirm.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the command is being processed.</param>
    /// <returns>
    /// Returns <c>200</c> when the request is accepted, <c>400</c> for invalid payload,
    /// <c>401</c> when the caller is not authenticated, or <c>404</c> when the account cannot be found.
    /// </returns>
    [HttpPost("request-email-change")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RequestEmailChange([FromBody] RequestEmailChangeRequestDto request, CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(Response<object>.Error("AUTHENTICATED_USER_REQUIRED"));
        }

        var response = await _sender.Send(new RequestEmailChangeCommand
        {
            UserId = userId,
            NewEmail = request.NewEmail
        }, cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Returns the list of active sessions associated with the authenticated user.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the request while the query is being processed.</param>
    /// <returns>
    /// Returns <c>200</c> with active session data, <c>400</c> for invalid request state,
    /// <c>401</c> when the caller is not authenticated, or <c>404</c> when no session source exists for the user.
    /// </returns>
    [HttpGet("sessions")]
    [ProducesResponseType(typeof(Response<IReadOnlyCollection<ActiveSessionDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSessions(CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized(Response<object>.Error("AUTHENTICATED_USER_REQUIRED"));
        }

        var response = await _sender.Send(new GetMySessionsQuery(userId), cancellationToken);
        return Ok(response);
    }

    /// <summary>
    /// Tries to resolve the current authenticated user identifier from JWT claims.
    /// </summary>
    /// <param name="userId">When this method returns, contains the parsed user identifier if available.</param>
    /// <returns><see langword="true"/> when a valid user identifier is present; otherwise <see langword="false"/>.</returns>
    private bool TryGetCurrentUserId(out Guid userId)
    {
        return Guid.TryParse(_currentUserService.UserId, out userId);
    }
}