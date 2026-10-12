using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake.Queries.LookupIntakePerson;

/// <summary>
/// <c>GET /CalendarChannelIntake/persons/lookup</c> (SPEC 45): the person who writes, by the channel identifier
/// (<see cref="ChannelCode"/> + <see cref="Identifier"/>) or by the identification pair. One of the two pairs is
/// required (<c>CALENDAR_INTAKE_LOOKUP_CRITERIA_REQUIRED</c>).
/// </summary>
public sealed record LookupIntakePersonQuery(
    string? ChannelCode,
    string? Identifier,
    Guid? IdentificationTypeId,
    string? IdentificationNumber)
    : IRequest<Response<IntakePersonLookupDto>>;
