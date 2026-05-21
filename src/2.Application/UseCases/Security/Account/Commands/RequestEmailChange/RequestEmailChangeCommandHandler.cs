using System.Net;
using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Domain.Security;
using MediatR;
using Microsoft.AspNetCore.Identity;

namespace JOIN.Application.UseCases.Security.Account.Commands.RequestEmailChange;

public sealed class RequestEmailChangeCommandHandler(
    UserManager<ApplicationUser> userManager,
    IEmailService emailService)
    : IRequestHandler<RequestEmailChangeCommand, Response<bool>>
{
    public async Task<Response<bool>> Handle(RequestEmailChangeCommand request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(request.UserId.ToString());
        if (user is null || !user.IsActive || user.GcRecord != 0)
        {
            return Response<bool>.Error("ACCOUNT_NOT_FOUND", ["Authenticated account was not found."]);
        }

        var newEmail = request.NewEmail.Trim();
        if (string.IsNullOrWhiteSpace(newEmail))
        {
            return Response<bool>.Error("INVALID_EMAIL", ["A valid email is required."]);
        }

        if (string.Equals(user.Email, newEmail, StringComparison.OrdinalIgnoreCase))
        {
            return Response<bool>.Error("EMAIL_UNCHANGED", ["New email must be different from current email."]);
        }

        var existingUser = await userManager.FindByEmailAsync(newEmail);
        if (existingUser is not null && existingUser.Id != user.Id)
        {
            return Response<bool>.Error("EMAIL_ALREADY_IN_USE", ["The requested email is already in use."]);
        }

        var token = await userManager.GenerateChangeEmailTokenAsync(user, newEmail);
        var encodedToken = WebUtility.UrlEncode(token);

        var subject = "Confirm your email change";
        var htmlBody = $"""
            <p>Hello {WebUtility.HtmlEncode(user.FirstName)},</p>
            <p>We received a request to change your account email in JOIN CRM.</p>
            <p>Use this confirmation token to complete the change:</p>
            <p><strong>{WebUtility.HtmlEncode(encodedToken)}</strong></p>
            <p>Requested email: <strong>{WebUtility.HtmlEncode(newEmail)}</strong></p>
            """;

        var sent = await emailService.SendEmailAsync(newEmail, subject, htmlBody);
        if (!sent)
        {
            return Response<bool>.Error("EMAIL_DELIVERY_FAILED", ["Unable to send the email change confirmation message."]);
        }

        return new Response<bool>
        {
            IsSuccess = true,
            Message = "Email change confirmation has been sent.",
            Data = true
        };
    }
}
