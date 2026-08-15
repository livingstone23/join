using JOIN.Application.Common;
using JOIN.Application.DTO.Security.Account;
using MediatR;

namespace JOIN.Application.UseCases.Security.Account.Commands.SetupMfa;

/// <summary>
/// Backs <c>POST /api/v1/account/mfa/setup</c> (SPEC 26 / F6 step 5). Generates the
/// Base32 secret, mints 10 hashed recovery codes, persists the secret on the user and
/// returns the QR provisioning URI + plaintext recovery codes. MFA is NOT enabled here
/// (enable is a separate explicit call).
/// </summary>
public sealed class SetupMfaCommand : ITransactionalCommand<Response<SetupMfaResponseDto>>;
