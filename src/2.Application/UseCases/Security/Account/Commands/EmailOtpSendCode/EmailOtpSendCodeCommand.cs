using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Security.Account.Commands.EmailOtpSendCode;

/// <summary>
/// Backs <c>POST /api/v1/account/email-otp/send-code</c> (SPEC 32 / F7). Invalidates any prior
/// codes, mints a fresh 6-digit code, persists it (PBKDF2-hashed) and emails it to the caller's
/// confirmed address. No fields — always targets the authenticated caller's own account.
/// </summary>
public sealed record EmailOtpSendCodeCommand : IRequest<Response<bool>>;
