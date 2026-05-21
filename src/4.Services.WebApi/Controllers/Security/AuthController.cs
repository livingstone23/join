using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Auth;
using JOIN.Application.UseCases.Security.Auth.ForgotPassword;
using JOIN.Application.UseCases.Security.Auth.ResetPassword;
using JOIN.Application.UseCases.Security.Auth.SetupPassword;
using JOIN.Services.WebApi.Filters;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;



namespace JOIN.Services.WebApi.Controllers.Security;



/// <summary>
/// Exposes perimeter-security authentication endpoints for account activation and password recovery.
/// The controller is intentionally thin and delegates all processing to the Application layer through MediatR.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
[PermissionResource("Users")]
public class AuthController(ISender sender) : ControllerBase
{
    private readonly ISender _sender = sender ?? throw new ArgumentNullException(nameof(sender));

    // Existing endpoints are intentionally not regenerated here.
    // Mapping to current implementation:
    // - POST api/v1/users/login
    // - POST api/v1/users/refresh
    // - POST api/v1/users/logout

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
            Token = request.Token,
            NewPassword = request.NewPassword,
            ConfirmPassword = request.ConfirmPassword
        }, cancellationToken);

        return Ok(response);
    }
}