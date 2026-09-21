using JOIN.Application.Common;
using JOIN.Application.DTO.Security;
using JOIN.Application.DTO.Security.Auth;
using JOIN.Application.UseCases.Security.Auth.ForgotPassword;
using JOIN.Application.UseCases.Security.Auth.MfaChallenge.SendMfaChallenge;
using JOIN.Application.UseCases.Security.Auth.MfaChallenge.VerifyMfaChallenge;
using JOIN.Application.UseCases.Security.Auth.ResetPassword;
using JOIN.Application.UseCases.Security.Auth.SetupPassword;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Asp.Versioning;



namespace JOIN.Services.WebApi.Controllers.Security;



/// <summary>
/// Exposes perimeter-security authentication endpoints for account activation and password recovery.
/// The controller is intentionally thin and delegates all processing to the Application layer through MediatR.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/auth")]
[Produces("application/json")]
[PermissionResource("Users")]
public class AuthController(ISender sender) : ControllerBase
{
    private readonly ISender _sender = sender ?? throw new ArgumentNullException(nameof(sender));

    // Existing endpoints are intentionally not regenerated here.
    // Mapping to current implementation:
    // - POST api/v{version:apiVersion}/users/login
    // - POST api/v{version:apiVersion}/users/refresh
    // - POST api/v{version:apiVersion}/users/logout

    /// <summary>
    /// Completes first-time account activation by setting the initial password through a valid setup token.
    /// </summary>
    /// <param name="request">The payload containing the setup token and the new password values.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the command is being processed.</param>
    /// <returns>
    /// Returns <c>200</c> when the setup operation is accepted, <c>400</c> for invalid payload/token,
    /// <c>401</c> when authentication context is rejected by security policies, or <c>404</c> when the token target user is not found.
    /// </returns>
    [AllowAnonymous]
    [HttpPost("setup-password")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetupPassword([FromBody] SetupPasswordRequestDto request, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new SetupPasswordCommand
        {
            Email = request.Email,
            Token = request.Token,
            NewPassword = request.NewPassword,
            ConfirmPassword = request.ConfirmPassword
        }, cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Starts the password recovery workflow by requesting a recovery token delivery to the account email.
    /// </summary>
    /// <param name="request">The payload containing the account email address.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the command is being processed.</param>
    /// <returns>
    /// Returns <c>200</c> when the request is accepted, <c>400</c> for invalid payload,
    /// <c>401</c> when rejected by security policies, or <c>404</c> when the account does not exist.
    /// </returns>
    [AllowAnonymous]
    [HttpPost("forgot-password")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequestDto request, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new ForgotPasswordCommand
        {
            Email = request.Email
        }, cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Resets the account password by validating a recovery token and applying the new password.
    /// </summary>
    /// <param name="request">The payload containing the recovery token and password values.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the command is being processed.</param>
    /// <returns>
    /// Returns <c>200</c> when the reset operation is accepted, <c>400</c> for invalid payload/token,
    /// <c>401</c> when rejected by security policies, or <c>404</c> when the token target user is not found.
    /// </returns>
    [AllowAnonymous]
    [HttpPost("reset-password")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequestDto request, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new ResetPasswordCommand
        {
            Email = request.Email,
            Token = request.Token,
            NewPassword = request.NewPassword,
            ConfirmPassword = request.ConfirmPassword
        }, cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// (Re)sends the email code for a pending MFA login challenge. Does not apply to
    /// <c>totp</c> — that method is validated live by the authenticator app.
    /// </summary>
    /// <param name="request">The payload carrying the opaque challenge token and the method to resend.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the command is being processed.</param>
    /// <returns>
    /// Returns <c>200</c> when the code is dispatched, <c>400</c> for a non-resendable/unavailable
    /// method, <c>401</c> when the challenge is unknown/expired, or <c>429</c> on resend cooldown.
    /// </returns>
    [AllowAnonymous]
    [EnableRateLimiting("Strict")]
    [HttpPost("mfa/challenge/send")]
    [ProducesResponseType(typeof(Response<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SendMfaChallenge([FromBody] SendMfaChallengeRequestDto request, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new SendMfaChallengeCommand
        {
            ChallengeToken = request.ChallengeToken,
            Method = request.Method
        }, cancellationToken);

        return Translate(response);
    }

    /// <summary>
    /// Verifies a pending MFA login challenge's code (live TOTP or the emailed code) and, only
    /// on success, resolves company/roles and issues the real JWT.
    /// </summary>
    /// <param name="request">The payload carrying the opaque challenge token, method, and code.</param>
    /// <param name="cancellationToken">Token used to cancel the request while the command is being processed.</param>
    /// <returns>
    /// Returns <c>200</c> with the JWT session on success, <c>400</c> for an unavailable method,
    /// <c>401</c> when the challenge/code is invalid, unknown, or expired, or <c>429</c> after 5
    /// failed attempts (the whole challenge is invalidated).
    /// </returns>
    [AllowAnonymous]
    [EnableRateLimiting("Strict")]
    [HttpPost("mfa/challenge/verify")]
    [ProducesResponseType(typeof(Response<LoginResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(Response<object>), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> VerifyMfaChallenge([FromBody] VerifyMfaChallengeRequestDto request, CancellationToken cancellationToken = default)
    {
        var response = await _sender.Send(new VerifyMfaChallengeCommand
        {
            ChallengeToken = request.ChallengeToken,
            Method = request.Method,
            Code = request.Code
        }, cancellationToken);

        return Translate(response);
    }

    /// <summary>
    /// Translates a <see cref="Response{T}"/> from a business failure into the HTTP status
    /// documented in the SPEC 26/32 error tables. Successful responses stay <c>200</c>.
    /// </summary>
    private IActionResult Translate<T>(Response<T> response)
    {
        if (response.IsSuccess)
        {
            return Ok(response);
        }

        return AccountErrorCodes.ToResult(response);
    }
}