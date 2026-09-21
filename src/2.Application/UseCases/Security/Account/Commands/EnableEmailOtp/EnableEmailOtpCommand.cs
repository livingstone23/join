using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Security.Account.Commands.EnableEmailOtp;

/// <summary>
/// Backs <c>POST /api/v1/account/email-otp/enable</c> (SPEC 32 / F7). Validates the code emitted
/// by <c>email-otp/send-code</c> and flips <c>IsEmailOtpEnabled</c> on success.
/// </summary>
/// <param name="Code">6-digit code emailed by <c>send-code</c>.</param>
public sealed record EnableEmailOtpCommand(string Code) : ITransactionalCommand<Response<bool>>;
