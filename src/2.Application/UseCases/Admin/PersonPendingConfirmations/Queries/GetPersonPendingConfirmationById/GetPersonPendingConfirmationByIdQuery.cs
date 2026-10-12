using System;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonPendingConfirmations.Queries.GetPersonPendingConfirmationById;

/// <summary>
/// Detail of a single pending, with the person's active contacts and the activities that target them.
/// </summary>
public sealed record GetPersonPendingConfirmationByIdQuery(
    Guid CompanyId,
    Guid PendingConfirmationId) : IRequest<Response<PersonPendingConfirmationDetailDto>>;
