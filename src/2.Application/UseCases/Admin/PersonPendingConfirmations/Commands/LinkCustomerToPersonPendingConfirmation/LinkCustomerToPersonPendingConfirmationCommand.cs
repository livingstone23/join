using System;
using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonPendingConfirmations.Commands.LinkCustomerToPersonPendingConfirmation;

/// <summary>
/// Closes a pending by linking it to a <c>Customer</c> of the same person. The <c>Customer</c>
/// must exist in the company and its <c>PersonId</c> must match the pending's person
/// (SPEC 45 F5).
/// </summary>
public sealed record LinkCustomerToPersonPendingConfirmationCommand(
    Guid CompanyId,
    Guid PendingConfirmationId,
    Guid CustomerId) : IRequest<Response<LinkCustomerToPersonPendingConfirmationDto>>, ITransactionalCommand<LinkCustomerToPersonPendingConfirmationDto>;
