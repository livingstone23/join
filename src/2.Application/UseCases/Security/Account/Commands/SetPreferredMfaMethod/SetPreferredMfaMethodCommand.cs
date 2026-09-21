using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Security.Account.Commands.SetPreferredMfaMethod;

/// <summary>
/// Backs <c>PUT /api/v1/account/mfa/preferred-method</c> (SPEC 32 / F8). Persists
/// <c>PreferredMfaMethod</c> on the caller, rejecting a method that is not currently active
/// for them.
/// </summary>
/// <param name="Method">The method to set as preferred ("email" | "totp").</param>
public sealed record SetPreferredMfaMethodCommand(string Method) : ITransactionalCommand<Response<bool>>;
