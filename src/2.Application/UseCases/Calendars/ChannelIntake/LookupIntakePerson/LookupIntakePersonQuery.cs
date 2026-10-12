using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake.LookupIntakePerson;

/// <summary>
/// Looks up a person the agent can address: by a phone/e-mail identifier from the channel, or by an
/// identification pair (SPEC 45). Exactly one of <see cref="ChannelCode"/>+<see cref="Identifier"/> or
/// <see cref="IdentificationTypeId"/>+<see cref="IdentificationNumber"/> is expected; the handler raises a
/// validation error when both are missing.
/// </summary>
public sealed record LookupIntakePersonQuery(
    string? ChannelCode,
    string? Identifier,
    Guid? IdentificationTypeId,
    string? IdentificationNumber)
    : IRequest<Response<IntakePersonLookupDto>>;