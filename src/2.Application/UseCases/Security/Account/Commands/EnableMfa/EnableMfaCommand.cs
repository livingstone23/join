using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Account;
using MediatR;

namespace JOIN.Application.UseCases.Security.Account.Commands.EnableMfa;

/// <summary>
/// Backs <c>POST /api/v1/account/mfa/enable</c> (SPEC 26 / F6 step 6). Validates a fresh
/// TOTP code against the persisted secret and flips <c>IsMfaEnabled</c> on success.
/// </summary>
/// <param name="Code">6-digit TOTP code from the authenticator app.</param>
public sealed record EnableMfaCommand(string Code) : ITransactionalCommand<Response<bool>>;
