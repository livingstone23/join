using JOIN.Application.Common;
using JOIN.Application.Common.Email;
using JOIN.Application.Common.Options;
using JOIN.Application.Interface;
using JOIN.Domain.Security;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace JOIN.Application.UseCases.Security.Auth.ForgotPassword;

/// <summary>
/// Starts a password recovery flow. Generates an Identity password-reset token and emails
/// a link to the user. Returns success even when the email is unknown or the account is
/// inactive to prevent account enumeration.
/// </summary>
/// <param name="userManager">ASP.NET Identity manager.</param>
/// <param name="emailService">Email adapter (SendGrid).</param>
/// <param name="appUrls">Bound <see cref="AppUrlsOptions"/>.</param>
public sealed class ForgotPasswordCommandHandler(
    UserManager<ApplicationUser> userManager,
    IEmailService emailService,
    IOptions<AppUrlsOptions> appUrls)
    : IRequestHandler<ForgotPasswordCommand, Response<bool>>
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly IEmailService _emailService = emailService;
    private readonly AppUrlsOptions _appUrls = appUrls.Value;

    public async Task<Response<bool>> Handle(ForgotPasswordCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(email))
        {
            // Do not reveal whether the email exists — return success.
            return new Response<bool> { IsSuccess = true, Message = "If the email exists, a reset link has been sent.", Data = true };
        }

        var user = await _userManager.FindByEmailAsync(email);
        if (user is null || !user.IsActive || user.GcRecord != 0)
        {
            return new Response<bool> { IsSuccess = true, Message = "If the email exists, a reset link has been sent.", Data = true };
        }

        if (string.IsNullOrWhiteSpace(_appUrls.FrontendBaseUrl))
        {
            return Response<bool>.Error("FRONTEND_URL_NOT_CONFIGURED", ["Frontend base URL is not configured."]);
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var link = AuthEmailTemplates.BuildLink(_appUrls.FrontendBaseUrl, AuthEmailTemplates.ResetPasswordPathName, email, token);

        var htmlBody = AuthEmailTemplates.BuildForgotPassword(user.FirstName ?? email, link);
        var sent = await _emailService.SendEmailAsync(email, "Reset your JOIN CRM password", htmlBody);
        if (!sent)
        {
            return Response<bool>.Error("EMAIL_DELIVERY_FAILED", ["Unable to send the password reset email."]);
        }

        return new Response<bool> { IsSuccess = true, Message = "If the email exists, a reset link has been sent.", Data = true };
    }
}
