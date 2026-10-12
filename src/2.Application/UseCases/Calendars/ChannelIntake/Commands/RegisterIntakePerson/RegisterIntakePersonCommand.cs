using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake;

/// <summary>
/// <c>POST /persons</c>: registers a new person (<c>Person</c> + <c>PersonContact</c> +
/// <c>PersonPendingConfirmation</c>) and a <c>PERSON_REVIEW</c> activity in the calendar of the company's
/// default user (SPEC 45 F4). Single transaction: the duplicate-by-identification check short-circuits with
/// <c>Created = false</c> and no inserts when an active person with the same identification already exists.
/// </summary>
public record RegisterIntakePersonCommand(
    Guid CompanyId,
    string OriginChannelCode,
    string OriginIdentifier,
    string OriginReference,
    int PersonType,
    string FirstName,
    string? LastName,
    string? CommercialName,
    Guid IdentificationTypeId,
    string IdentificationNumber,
    Guid? GenderId,
    string? Phone,
    string? Email,
    string Reason)
    : IRequest<Response<RegisterIntakePersonDto>>, ITransactionalCommand<RegisterIntakePersonDto>;