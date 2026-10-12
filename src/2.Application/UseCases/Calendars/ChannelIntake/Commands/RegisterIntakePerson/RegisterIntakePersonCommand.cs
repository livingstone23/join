using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Domain.Enums;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake.Commands.RegisterIntakePerson;

/// <summary>
/// <c>POST /CalendarChannelIntake/persons</c> (SPEC 45): registers a person the agent does not know —
/// <c>Person</c> + <c>PersonContact</c> + <c>PersonPendingConfirmation</c> — and a <c>PERSON_REVIEW</c> activity in the
/// calendar of the company's default user. An active person with the same identification is returned as is, without
/// creating or changing anything. The company is the one of the token.
/// </summary>
public sealed record RegisterIntakePersonCommand(
    PersonType PersonType,
    string FirstName,
    string? LastName,
    string? CommercialName,
    Guid IdentificationTypeId,
    string IdentificationNumber,
    Guid? GenderId,
    string? Phone,
    string? Email,
    string Reason,
    string OriginChannelCode,
    string OriginIdentifier,
    string? OriginReference)
    : ITransactionalCommand<Response<RegisterIntakePersonDto>>;
