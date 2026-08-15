using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Security.Account.Commands.ConfirmEmailChange;

/// <summary>
/// Backs <c>POST /api/v1/account/confirm-email-change</c> (SPEC 26 / F7). Calls Identity's
/// <c>ChangeEmailAsync</c> against the supplied token. Not transactional — Identity writes
/// the email change and the matched-token row in its own atomic step.
/// </summary>
/// <param name="NewEmail">The intended new email address.</param>
/// <param name="Token">The raw token delivered by <c>request-email-change</c>.</param>
public sealed record ConfirmEmailChangeCommand(string NewEmail, string Token) : IRequest<Response<bool>>;
