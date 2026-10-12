using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;

namespace JOIN.Application.UseCases.Admin.PersonPendingConfirmations.Commands.LinkCustomerToPersonPendingConfirmation;

/// <summary>
/// <c>POST /PersonPendingConfirmations/{id}/link-customer</c> (SPEC 45): links the pending to an existing
/// <c>Customer</c> of the same person and closes it as <c>LinkedToCustomer</c>. Only from <c>Pending</c>.
/// </summary>
public sealed record LinkCustomerToPersonPendingConfirmationCommand(
    Guid PendingConfirmationId,
    Guid CustomerId) : ITransactionalCommand<Response<LinkCustomerToPersonPendingConfirmationDto>>;
