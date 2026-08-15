using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Security.Account.Commands.DisableMfa;

/// <summary>
/// Backs <c>POST /api/v1/account/mfa/disable</c> (SPEC 26 / F6 step 7). Accepts either a
/// fresh TOTP code or a recovery code. Clears MFA state and the recovery-code set on success.
/// </summary>
/// <param name="Code">6-digit TOTP code or 10-character recovery code.</param>
public sealed record DisableMfaCommand(string Code) : ITransactionalCommand<Response<bool>>;
