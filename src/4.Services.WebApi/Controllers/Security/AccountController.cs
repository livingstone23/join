using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Account;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Security.Account.Commands.ChangeMyPassword;
using JOIN.Application.UseCases.Security.Account.Commands.ConfirmEmailChange;
using JOIN.Application.UseCases.Security.Account.Commands.DisableMfa;
using JOIN.Application.UseCases.Security.Account.Commands.EnableMfa;
using JOIN.Application.UseCases.Security.Account.Commands.ConfirmPhoneVerification;
using JOIN.Application.UseCases.Security.Account.Commands.RequestEmailChange;
using JOIN.Application.UseCases.Security.Account.Commands.RequestPhoneVerification;
using JOIN.Application.UseCases.Security.Account.Commands.RevokeOtherMySessions;
using JOIN.Application.UseCases.Security.Account.Commands.RevokeMySession;
using JOIN.Application.UseCases.Security.Account.Commands.SetupMfa;
using JOIN.Application.UseCases.Security.Account.Commands.UpdateMyProfile;
using JOIN.Application.UseCases.Security.Account.Queries.GetMyPermissions;
using JOIN.Application.UseCases.Security.Account.Queries.GetMyProfile;
using JOIN.Application.UseCases.Security.Account.Queries.GetSecurityActivity;
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
    /// Revokes a single session belonging to the authenticated caller.
    /// </summary>
    /// <param name="sessionId">Session id (UserConnectionLogs or UserRefreshTokens row).</param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    /// <returns>
    /// Returns <c>200</c> with counters, <c>400</c> when the supplied id targets the current
    /// refresh token, <c>401</c> when unauthenticated, or <c>404</c> when the session does
    /// not exist or belongs to another user.
    /// </returns>
    [HttpDelete("sessions/{sessionId:guid}")]
    [ProducesResponseType(typeof(Response<RevokeMySessionResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeMySession(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new RevokeMySessionCommand(sessionId), cancellationToken);
        return Translate(response);
    }

    /// <summary>
    /// Revokes every other session for the authenticated caller except the current one.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    /// <returns>
    /// Returns <c>200</c> with the counters, or <c>401</c> when unauthenticated.
    /// </returns>
    [HttpPost("sessions/revoke-others")]
    [ProducesResponseType(typeof(Response<RevokeOtherMySessionsResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RevokeOtherMySessions(CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new RevokeOtherMySessionsCommand(), cancellationToken);
        return Translate(response);
    }

    /// <summary>
    /// Returns the combined permissions matrix for the caller inside the JWT-resolved tenant.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    /// <returns>
    /// Returns <c>200</c> with the matrix, <c>400</c> when the tenant cannot be resolved, or
    /// <c>401</c> when unauthenticated.
    /// </returns>
    [HttpGet("my-permissions")]
    [ProducesResponseType(typeof(Response<MyPermissionsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyPermissions(CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new GetMyPermissionsQuery(), cancellationToken);
        return Translate(response);
    }

    /// <summary>
    /// Mints a fresh MFA enrolment bundle (Base32 secret + provisioning URI + recovery codes).
    /// Does NOT enable MFA — the caller must POST <c>/mfa/enable</c> with a TOTP code to activate.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    /// <returns>
    /// Returns <c>200</c> with the enrolment bundle, <c>401</c> when unauthenticated, or
    /// <c>404</c> when the account cannot be found.
    /// </returns>
    [HttpPost("mfa/setup")]
    [ProducesResponseType(typeof(Response<SetupMfaResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetupMfa(CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new SetupMfaCommand(), cancellationToken);
        return Translate(response);
    }

    /// <summary>
    /// Confirms a previously issued MFA enrolment by validating the supplied TOTP code.
    /// </summary>
    /// <param name="request">Body carrying the 6-digit TOTP code.</param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    /// <returns>
    /// Returns <c>200</c> on success, <c>400</c> when MFA is not configured or the code is invalid,
    /// <c>401</c> when unauthenticated, or <c>404</c> when the account cannot be found.
    /// </returns>
    [HttpPost("mfa/enable")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EnableMfa([FromBody] EnableMfaRequestDto request, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new EnableMfaCommand(request.Code), cancellationToken);
        return Translate(response);
    }

    /// <summary>
    /// Disables MFA via either a fresh TOTP code or a recovery code.
    /// </summary>
    /// <param name="request">Body carrying the TOTP or recovery code.</param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    /// <returns>
    /// Returns <c>200</c> on success, <c>400</c> when MFA is not configured or the code is invalid,
    /// <c>401</c> when unauthenticated, or <c>404</c> when the account cannot be found.
    /// </returns>
    [HttpPost("mfa/disable")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DisableMfa([FromBody] DisableMfaRequestDto request, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new DisableMfaCommand(request.Code), cancellationToken);
        return Translate(response);
    }

    /// <summary>
    /// Confirms a pending email change using the token delivered by <c>request-email-change</c>.
    /// </summary>
    /// <param name="request">Body carrying the new email and the token from the request step.</param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    /// <returns>
    /// Returns <c>200</c> on success, <c>400</c> when the token is invalid or mismatched,
    /// <c>401</c> when unauthenticated, or <c>404</c> when the account cannot be found.
    /// </returns>
    [HttpPost("confirm-email-change")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ConfirmEmailChange([FromBody] ConfirmEmailChangeRequestDto request, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new ConfirmEmailChangeCommand(request.NewEmail, request.Token), cancellationToken);
        return Translate(response);
    }

    /// <summary>
    /// Requests a fresh SMS verification code for an E.164 phone number.
    /// </summary>
    /// <param name="request">Body carrying the E.164 phone number.</param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    /// <returns>
    /// Returns <c>200</c> on dispatch, <c>400</c> when the phone is not E.164-formatted,
    /// or <c>401</c> when unauthenticated.
    /// </returns>
    [HttpPost("verify-phone/request")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RequestPhoneVerification([FromBody] RequestPhoneVerificationRequestDto request, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new RequestPhoneVerificationCommand(request.PhoneNumber), cancellationToken);
        return Translate(response);
    }

    /// <summary>
    /// Confirms the SMS-delivered 6-digit code and flips PhoneNumberConfirmed on the caller.
    /// </summary>
    /// <param name="request">Body carrying the 6-digit code.</param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    /// <returns>
    /// Returns <c>200</c> on success, <c>400</c> when the code is expired/invalid, <c>429</c>
    /// when the code is locked (5+ failed attempts), or <c>401</c> when unauthenticated.
    /// </returns>
    [HttpPost("verify-phone/confirm")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ConfirmPhoneVerification([FromBody] ConfirmPhoneVerificationRequestDto request, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new ConfirmPhoneVerificationCommand(request.Code), cancellationToken);
        return Translate(response);
    }

    /// <summary>
    /// Returns the caller's paginated security-activity feed.
    /// </summary>
    /// <param name="pageNumber">1-based page number (clamped to &gt;= 1).</param>
    /// <param name="pageSize">Page size (clamped to 1..100).</param>
    /// <param name="cancellationToken">Token used to cancel the request.</param>
    /// <returns>
    /// Returns <c>200</c> with the paged feed, or <c>401</c> when unauthenticated.
    /// </returns>
    [HttpGet("security-activity")]
    [ProducesResponseType(typeof(Response<PagedResult<SecurityActivityEventDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetSecurityActivity(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new GetSecurityActivityQuery(pageNumber, pageSize), cancellationToken);
        return Translate(response);
    }

    /// <summary>
    /// Translates a <see cref="Response{T}"/> from a SPEC 26 business failure into the
    /// HTTP status documented in the F11 error table. Successful responses stay <c>200</c>.
    /// </summary>
    private IActionResult Translate<T>(Response<T> response)
    {
        if (response.IsSuccess)
        {
            return Ok(response);
        }

        return AccountErrorCodes.ToResult(response);
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