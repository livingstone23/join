using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Admin;
using JOIN.Domain.Enums;

namespace JOIN.Application.UseCases.Admin.PersonPendingConfirmations;

/// <summary>
/// Loads a pending confirmation that can still be closed (SPEC 45): it exists in the company
/// (<c>PERSON_PENDING_NOT_FOUND</c>) and is <c>Pending</c> (<c>PERSON_PENDING_ALREADY_RESOLVED</c>).
/// </summary>
public sealed class PersonPendingConfirmationReader(IUnitOfWork unitOfWork)
{
    public async Task<Response<PersonPendingConfirmation>> OpenPendingAsync(Guid companyId, Guid pendingConfirmationId)
    {
        var pending = (await unitOfWork.GetRepository<PersonPendingConfirmation>()
                .GetAllIncludingDeletedAsync(x => x.Id == pendingConfirmationId && x.CompanyId == companyId && x.GcRecord == 0))
            .FirstOrDefault();
        if (pending is null)
        {
            return Response<PersonPendingConfirmation>.Error(CalendarErrorCodes.PersonPendingNotFound, ["The pending confirmation was not found in the current company."]);
        }

        return pending.Status == PersonPendingConfirmationStatus.Pending
            ? new Response<PersonPendingConfirmation> { IsSuccess = true, Data = pending }
            : Response<PersonPendingConfirmation>.Error(CalendarErrorCodes.PersonPendingAlreadyResolved, ["Only a Pending confirmation can be closed."]);
    }
}
