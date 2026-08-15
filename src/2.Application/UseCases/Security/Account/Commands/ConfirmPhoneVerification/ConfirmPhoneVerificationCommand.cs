using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Security.Account.Commands.ConfirmPhoneVerification;

/// <summary>
/// Backs <c>POST /api/v1/account/verify-phone/confirm</c> (SPEC 26 / F8 step 5).
/// Validates the supplied 6-digit code, sets <c>PhoneNumberConfirmed = true</c>,
/// and emits the right audit event.
/// </summary>
/// <param name="Code">6-digit numeric code delivered to the requested phone number.</param>
public sealed record ConfirmPhoneVerificationCommand(string Code) : ITransactionalCommand<Response<bool>>;
