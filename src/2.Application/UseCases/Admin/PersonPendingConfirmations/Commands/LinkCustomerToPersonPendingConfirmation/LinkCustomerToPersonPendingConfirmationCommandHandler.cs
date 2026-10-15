using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Admin;
using JOIN.Domain.Enums;
using MediatR;
using JOIN.Domain.Customers;

namespace JOIN.Application.UseCases.Admin.PersonPendingConfirmations.Commands.LinkCustomerToPersonPendingConfirmation;

/// <summary>
/// Handles <see cref="LinkCustomerToPersonPendingConfirmationCommand"/> (SPEC 45): the customer must exist in the
/// company and belong to the pending's person (<c>PERSON_PENDING_INVALID_CUSTOMER</c>); the pending closes as
/// <c>LinkedToCustomer</c> and the review activity is completed (<see cref="PersonPendingReviewCloser"/>).
/// </summary>
public sealed class LinkCustomerToPersonPendingConfirmationCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    PersonPendingConfirmationReader reader,
    PersonPendingReviewCloser reviewCloser)
    : IRequestHandler<LinkCustomerToPersonPendingConfirmationCommand, Response<LinkCustomerToPersonPendingConfirmationDto>>
{
    public async Task<Response<LinkCustomerToPersonPendingConfirmationDto>> Handle(LinkCustomerToPersonPendingConfirmationCommand request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<LinkCustomerToPersonPendingConfirmationDto>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!Guid.TryParse(currentUserService.UserId, out var userId) || userId == Guid.Empty)
        {
            return Response<LinkCustomerToPersonPendingConfirmationDto>.Error(CalendarErrorCodes.UserRequired, ["The authenticated user could not be resolved."]);
        }

        var opened = await reader.OpenPendingAsync(companyId, request.PendingConfirmationId);
        if (!opened.IsSuccess)
        {
            return Response<LinkCustomerToPersonPendingConfirmationDto>.Error(opened.Message, opened.Errors);
        }

        var pending = opened.Data!;
        var customer = (await unitOfWork.GetRepository<Customer>()
                .GetAllIncludingDeletedAsync(x => x.Id == request.CustomerId && x.CompanyId == companyId && x.GcRecord == 0))
            .FirstOrDefault();
        if (customer is null || customer.PersonId != pending.PersonId)
        {
            return Response<LinkCustomerToPersonPendingConfirmationDto>.Error(CalendarErrorCodes.PersonPendingInvalidCustomer,
                ["The customer does not exist in the company or belongs to another person."]);
        }

        pending.Status = PersonPendingConfirmationStatus.LinkedToCustomer;
        pending.CustomerId = customer.Id;
        pending.ResolvedByUserId = userId;
        pending.ResolvedUtc = DateTime.UtcNow;
        pending.ResolutionNotes = $"Vinculada al cliente {customer.CustomerCode}";
        await unitOfWork.GetRepository<PersonPendingConfirmation>().UpdateAsync(pending);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var closed = await reviewCloser.CloseAsync(pending, cancellationToken);
        if (!closed.IsSuccess)
        {
            return Response<LinkCustomerToPersonPendingConfirmationDto>.Error(closed.Message, closed.Errors);
        }

        return new Response<LinkCustomerToPersonPendingConfirmationDto>
        {
            IsSuccess = true,
            Data = new LinkCustomerToPersonPendingConfirmationDto(pending.Id, customer.Id),
            Message = "Person pending confirmation linked to the customer successfully."
        };
    }
}
