using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonPendingConfirmations.Queries.GetPersonPendingConfirmationById;

/// <summary>
/// <c>GET /PersonPendingConfirmations/{id}</c> (SPEC 45): the pending with the person's active contacts and the
/// non-cancelled activities whose requester is the person.
/// </summary>
public sealed record GetPersonPendingConfirmationByIdQuery(Guid PendingConfirmationId)
    : IRequest<Response<PersonPendingConfirmationDetailDto>>;
