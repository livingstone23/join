using JOIN.Application.Common;
using MediatR;

namespace JOIN.Application.UseCases.Security.Account.Commands.RequestPhoneVerification;

/// <summary>
/// Backs <c>POST /api/v1/account/verify-phone/request</c> (SPEC 26 / F8 step 4).
/// Invalidates any prior codes, mints a fresh 6-digit verification code, persists it
/// (PBKDF2-hashed) and pushes it through <see cref="ISmsService"/>.
/// </summary>
/// <param name="PhoneNumber">E.164-formatted destination phone number.</param>
public sealed record RequestPhoneVerificationCommand(string PhoneNumber) : IRequest<Response<bool>>;
